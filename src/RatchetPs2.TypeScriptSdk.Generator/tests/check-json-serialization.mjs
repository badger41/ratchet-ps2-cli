import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const sourcePath = new URL(`../../../src/RatchetPs2.Core/SdkJsonSerializationCheck${process.pid}.cs`, import.meta.url);
const dotnet = process.argv[2] ?? 'dotnet';
const source = `
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RatchetPs2;

public enum SdkJsonMode { First, Second }
public sealed record SdkJsonIgnored(int Value, [property: JsonIgnore] int Hidden);
public readonly record struct SdkJsonBoolRecord(bool Enabled);

public static class SdkJsonSerializationCheck {
    public static byte[] Serialize(bool indented, bool stringEnums) {
        var options = new JsonSerializerOptions { WriteIndented = indented };
        if (stringEnums) options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.SerializeToUtf8Bytes(new {
            Name = "map", Bytes = new byte[] { 0, 127, 255 }, Values = new[] { 1, 2 },
            Optional = (string?)null, Enabled = true, Boxed = (object)false,
            FromRecord = new SdkJsonBoolRecord(false).Enabled, Mode = SdkJsonMode.Second
        }, options);
    }
    public static byte[] Mutate(byte[] input) {
        var root = JsonNode.Parse(input)!.AsObject();
        var values = (JsonArray)root["values"]!;
        values.Add(3);
        root.Remove("remove");
        root["nested"] = new JsonObject { ["enabled"] = true };
        root["copy"] = values.DeepClone();
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }
    public static byte[] Ignore() => JsonSerializer.SerializeToUtf8Bytes(new SdkJsonIgnored(4, 9));
    public static byte[] Dictionary() => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?> {
        ["name"] = "map", ["values"] = new[] { 1, 2 }, ["enabled"] = false
    });
    public static byte[] Enumerable() => JsonSerializer.SerializeToUtf8Bytes(
        System.Linq.Enumerable.Range(1, 3).OrderByDescending(value => value).Select(value => new { Value = value }));
    public static byte[][] Cases() => new[] {
        Serialize(false, false), Serialize(true, false), Serialize(false, true),
        Mutate(System.Text.Encoding.UTF8.GetBytes("{\\"values\\":[1,2],\\"remove\\":true}")), Ignore(), Dictionary(), Enumerable()
    };
}
`;

writeFileSync(sourcePath, source, { flag: 'wx' });
try {
  const build = spawnSync(dotnet, ['run', '--no-restore', '--', '--type', 'RatchetPs2.SdkJsonSerializationCheck', '--verify'], { cwd, encoding: 'utf8' });
  assert.ifError(build.error);
  assert.equal(build.status, 0, build.stdout + build.stderr);
  const { RatchetPs2_SdkJsonSerializationCheck: api } = await import(`../bin/probe/index.js?${Date.now()}`);
  const expected = JSON.parse(readFileSync(new URL('../bin/probe/json-serialization.json', import.meta.url), 'utf8'))
    .map(value => Buffer.from(value, 'base64').toString('utf8'));
  const actual = [
    api.serialize(false, false), api.serialize(true, false), api.serialize(false, true),
    api.mutate(new TextEncoder().encode('{"values":[1,2],"remove":true}')), api.ignore(), api.dictionary(), api.enumerable()
  ].map(value => new TextDecoder().decode(value));
  assert.deepEqual(actual, expected);
  assert.throws(() => api.mutate(new TextEncoder().encode('null')));
  assert.throws(() => api.mutate(new Uint8Array([0xff])));
  console.log('JSON serialization parity passed: objects, dictionaries, bytes, indentation, enum strings, ignored members and mutable nodes.');
} finally {
  unlinkSync(sourcePath);
}
