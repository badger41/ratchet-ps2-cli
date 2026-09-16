import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { writeFileSync, unlinkSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const sourcePath = new URL(`../../../src/RatchetPs2.Core/SdkBclCheck${process.pid}.cs`, import.meta.url);
const dotnet = process.argv[2] ?? 'dotnet';
const source = `
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RatchetPs2.Core.Moby;

namespace RatchetPs2;

public sealed record SdkCallbackValue(int Value);
public enum SdkCollectionState { Missing, Exact }

public static partial class SdkBclCheck {
    public static bool[] ReadOnlyCollectionValues() {
        IReadOnlyList<SdkCollectionState> array = new[] { SdkCollectionState.Missing, SdkCollectionState.Exact };
        IReadOnlyList<SdkCollectionState> list = new List<SdkCollectionState> { SdkCollectionState.Exact };
        IReadOnlyList<bool> flags = new[] { true, false };
        IReadOnlyList<int> numbers = new[] { 7 };
        var index = 0;
        return new[] {
            IsExact(array[1]), IsExact(list[0]), !IsExact(array[0]),
            flags[index++] == true, index == 1, flags[1] == false,
            numbers[0] == 7, array[^1] switch { SdkCollectionState.Exact => true, _ => false }
        };
    }
    private static bool IsExact(SdkCollectionState value) => value is SdkCollectionState.Exact;
    [GeneratedRegex("^[a-z]+[0-9]+$")]
    private static partial Regex NamePattern();

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string PathValue(string value) => Path.Combine("folder", Path.ChangeExtension(Path.GetFileName(value), "bin"));
    public static string Sorted(int[] values) {
        var sorted = new SortedDictionary<int, int>();
        foreach (var value in values) sorted[value] = value;
        return string.Join(",", sorted.Keys);
    }
    public static bool RegexValue(string value) => NamePattern().IsMatch(value);
    public static int SpanValue(byte[] bytes) {
        ReadOnlySpan<byte> span = bytes;
        var sum = 0;
        foreach (var value in span) sum += value;
        return span.IndexOf((byte)3) * 100 + sum;
    }
    public static int QueueValue(int[] values) {
        var queue = new Queue<int>(values);
        var sum = 0;
        while (queue.TryDequeue(out var value)) sum += value;
        return sum;
    }
    public static byte[] ReadInput(IMobyModelInput input) {
        if (!input.FileExists("file.bin") || !input.DirectoryExists("folder")) return [];
        return input.ReadBytes(input.EnumerateFiles("folder")[0]);
    }
    public static void WriteOutput(IMobyModelOutput output, byte[] bytes) => output.WriteBytes("file.bin", bytes);
    public static byte[] Open(Func<string, Stream> open) {
        using var input = open("buffer.bin");
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
    public static int Select(Func<SdkCallbackValue, IReadOnlyList<SdkCallbackValue>> callback) =>
        callback(new SdkCallbackValue(7)).Sum(value => value.Value);
    public static void Notify(Action<string, string, double, string?> callback) => callback("id", "label", 1.5, null);
    public static object Metadata() => new { Name = "map", Values = new[] { 1, 2 } };
}
`;

writeFileSync(sourcePath, source, { flag: 'wx' });
try {
  const build = spawnSync(dotnet, ['run', '--no-restore', '--', '--type', 'RatchetPs2.SdkBclCheck'], { cwd, encoding: 'utf8' });
  assert.ifError(build.error);
  assert.equal(build.status, 0, build.stdout + build.stderr);
  const { RatchetPs2_SdkBclCheck: api } = await import(`../bin/probe/index.js?${Date.now()}`);

  const bytes = new Uint8Array([1, 2, 3, 4]);
  assert.deepEqual(api.readOnlyCollectionValues(), Array(8).fill(true));
  assert.equal(api.hash(bytes), createHash('sha256').update(bytes).digest('hex').toUpperCase());
  assert.equal(api.pathValue('/tmp/model.gltf'), 'folder/model.bin');
  assert.equal(api.sorted([3, 1, 2]), '1,2,3');
  assert.equal(api.regexValue('map42'), true);
  assert.equal(api.regexValue('MAP42'), false);
  assert.equal(api.spanValue(bytes), 210);
  assert.equal(api.queueValue([3, 4, 5]), 12);

  const input = {
    fileExists: path => path === 'file.bin',
    directoryExists: path => path === 'folder',
    readBytes: path => path === 'folder/data.bin' ? bytes : new Uint8Array(),
    enumerateDirectories: () => ['folder'],
    enumerateFiles: () => ['folder/data.bin']
  };
  assert.deepEqual(api.readInput(input), bytes);
  let written;
  api.writeOutput({ writeBytes: (path, value) => { written = [path, value]; } }, bytes);
  assert.equal(written[0], 'file.bin');
  assert.deepEqual(written[1], bytes);
  assert.deepEqual(api.open(path => path === 'buffer.bin' ? bytes.buffer : null), bytes);
  assert.equal(api.select(value => [{ value: value.value + 1 }]), 8);
  let timing;
  api.notify((...values) => { timing = values; });
  assert.deepEqual(timing, ['id', 'label', 1.5, null]);
  assert.deepEqual(api.metadata(), { Name: 'map', Values: [1, 2] });
  assert.throws(() => api.readInput({}), TypeError);
  assert.throws(() => api.open(() => null), TypeError);
  console.log('Remaining BCL and SDK boundary parity passed: typed collection reads, hashing, paths, sorting, regex, spans, queues, callbacks and byte-oriented interfaces.');
} finally {
  unlinkSync(sourcePath);
}
