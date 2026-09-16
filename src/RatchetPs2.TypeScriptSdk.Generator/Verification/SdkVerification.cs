using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Reflection;

namespace RatchetPs2.TypeScriptSdk.Generator;

internal static class Verification
{
    private delegate object ReadSettings(ReadOnlySpan<byte> data);
    private delegate object ReadGameplay(ReadOnlySpan<byte> data);

    public static void WriteUyaEndToEnd(
        string output,
        Assembly assembly,
        string wadPath,
        string zipPath)
    {
        var builder = assembly.GetType("RatchetPs2.Sdk.FrontendMapPackageBuilder")!;
        var buildWad = builder.GetMethod("BuildLevelWad")!;
        var buildZip = builder.GetMethod("BuildUyaCustomMapZip")!;
        var gameId = assembly.GetType("RatchetPs2.Core.Games.GameId")!;
        var uya = Enum.Parse(gameId, "UYA");

        var standard = buildWad.Invoke(null, [File.ReadAllBytes(wadPath), uya])!;
        var custom = buildZip.Invoke(null, [File.ReadAllBytes(zipPath)])!;
        WritePackedBytes(output, "uya-e2e-standard.bin", standard);
        WritePackedBytes(output, "uya-e2e-custom.bin", custom);
        File.WriteAllText(
            Path.Combine(output, "uya-e2e.json"),
            JsonSerializer.Serialize(new
            {
                standard = DescribeUyaPackage(standard, assembly),
                custom = DescribeUyaPackage(custom, assembly)
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    public static void Write(string output, Assembly assembly)
    {
        if (assembly.GetType("RatchetPs2.SdkBufferCheck")?.GetMethod("Cases") is { } bufferCases)
            File.WriteAllText(Path.Combine(output, "buffers.json"), JsonSerializer.Serialize(bufferCases.Invoke(null, null)));
        if (assembly.GetType("RatchetPs2.SdkArithmeticCheck")?.GetMethod("Cases") is { } arithmeticCases)
            File.WriteAllText(Path.Combine(output, "arithmetic.json"), JsonSerializer.Serialize(arithmeticCases.Invoke(null, null)));
        if (assembly.GetType("RatchetPs2.SdkNumericsCheck")?.GetMethod("Cases") is { } numericsCases)
            File.WriteAllText(Path.Combine(output, "numerics.json"), JsonSerializer.Serialize(numericsCases.Invoke(null, null)));
        if (assembly.GetType("RatchetPs2.SdkIoCheck")?.GetMethod("Cases") is { } ioCases)
        {
            File.WriteAllText(Path.Combine(output, "io.json"), JsonSerializer.Serialize(ioCases.Invoke(null, null)));
            WriteIoFixtures(output, assembly);
        }
        if (assembly.GetType("RatchetPs2.SdkJsonSerializationCheck")?.GetMethod("Cases") is { } jsonCases)
            File.WriteAllText(Path.Combine(output, "json-serialization.json"), JsonSerializer.Serialize(jsonCases.Invoke(null, null)));
        WriteJsonFixtures(output, assembly);
        var read = assembly.GetType("RatchetPs2.Games.UYA.Gameplay.UyaLevelSettingsReader")!
            .GetMethod("Read")!.CreateDelegate<ReadSettings>();
        var inputs = new List<byte[]> { new byte[0x84] };
        foreach (var planes in new[] { 0, 1, 2 })
        {
            var bytes = new byte[0x5c + Math.Max(1, planes) * 0x20 + 8 + 3];
            for (var offset = 0; offset + 4 <= bytes.Length; offset += 4)
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), offset * 37 - 4096);
            foreach (var offset in new[] { 0x18, 0x1c, 0x20, 0x24, 0x28, 0x30, 0x34, 0x38, 0x3c, 0x40, 0x44, 0x48 })
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), offset * -0.125f);
            for (var plane = 0; plane < Math.Max(1, planes); plane++)
                foreach (var offset in new[] { 0, 4, 8, 0x10, 0x14, 0x18 })
                    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x5c + plane * 0x20 + offset), plane + offset * 0.5f);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x68), planes);
            bytes[^3] = 0xab;
            bytes[^2] = 0xcd;
            bytes[^1] = 0xef;
            inputs.Add(bytes);
        }
        inputs.Add(inputs[1][..0x80]);
        inputs.Add(inputs[1][..0x82]);
        foreach (var count in new[] { 2, int.MaxValue, -1 })
        {
            var bytes = new byte[0x84];
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x68), count);
            inputs.Add(bytes);
        }
        inputs.Add([]);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var cases = inputs.Select(bytes =>
        {
            object? expected = null;
            string? error = null;
            try { expected = read(bytes); }
            catch (Exception ex) { error = ex.GetType().Name; }
            return new { input = bytes, expected, error };
        });
        File.WriteAllText(Path.Combine(output, "parity.json"), JsonSerializer.Serialize(cases, options));

        var integers = new[] { new byte[8], Enumerable.Repeat((byte)255, 8).ToArray(),
            new byte[] { 0x01, 0x80, 0x23, 0xff, 0x45, 0x67, 0x89, 0xab } };
        File.WriteAllText(Path.Combine(output, "integers.json"), JsonSerializer.Serialize(integers.Select(bytes => new
        {
            input = bytes,
            int16 = BinaryPrimitives.ReadInt16LittleEndian(bytes),
            uint16 = BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            int32 = BinaryPrimitives.ReadInt32LittleEndian(bytes),
            uint32 = BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            uint64 = BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString()
        }), options));
    }

    private static void WriteJsonFixtures(string output, Assembly assembly)
    {
        var inspect = assembly.GetType("RatchetPs2.Core.Gltf.GltfModelInspector")!
            .GetMethod("Inspect", [typeof(Stream)])!;
        var inputs = new[]
        {
            """{"meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],"accessors":[{"count":3,"min":[-1,-2,-3],"max":[1,2,3]},{"count":3}],"materials":[{}],"textures":[{}],"images":[{"uri":"a.png"},{"uri":null},{}]}""",
            "{}",
            """{"meshes":null,"materials":null,"textures":null,"images":null}""",
            "null",
            """{"meshes":[{"primitives":[{"mode":null}]}],"accessors":[]}""",
            """{"meshes":[]"""
        }.Select(System.Text.Encoding.UTF8.GetBytes).Concat([new byte[] { 0xff }]).ToArray();
        var cases = inputs.Select(bytes =>
        {
            object? expected = null;
            var error = false;
            try { expected = inspect.Invoke(null, [new MemoryStream(bytes)]); }
            catch (TargetInvocationException) { error = true; }
            return new { input = bytes, expected, error };
        });
        File.WriteAllText(Path.Combine(output, "json.json"), JsonSerializer.Serialize(cases, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static void WriteIoFixtures(string output, Assembly assembly)
    {
        var raw = new byte[] { 0, 10, 20, 30, 40 };
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);
        var zlibBytes = compressed.ToArray();
        var zip = CreateZip(("folder/map.wad", new byte[0x58]), ("folder/map.world", []));
        var missing = CreateZip(("map.wad", new byte[0x58]));
        var duplicate = CreateZip(("a.wad", new byte[0x58]), ("b.wad", new byte[0x58]), ("map.world", []));
        var truncatedZip = zip[..^8];
        var truncatedZlib = zlibBytes[..^4];
        var malformedZlib = zlibBytes.ToArray();
        malformedZlib[0] = 0;
        File.WriteAllText(Path.Combine(output, "io-compression.json"), JsonSerializer.Serialize(new
        {
            raw,
            zlib = zlibBytes,
            truncatedZlib,
            truncatedZlibError = Error(() => Inflate(truncatedZlib)),
            malformedZlib,
            malformedZlibError = Error(() => Inflate(malformedZlib)),
            zip,
            validZipError = ZipError(assembly, zip),
            missingZip = missing,
            missingZipError = ZipError(assembly, missing),
            duplicateZip = duplicate,
            duplicateZipError = ZipError(assembly, duplicate),
            truncatedZip,
            truncatedZipError = ZipError(assembly, truncatedZip)
        }));
    }

    private static object DescribeUyaPackage(object package, Assembly assembly)
    {
        var type = package.GetType();
        var packedBytes = (byte[])type.GetProperty("PackedBytes")!.GetValue(package)!;
        var entries = ((System.Collections.IEnumerable)type.GetProperty("Entries")!.GetValue(package)!)
            .Cast<object>()
            .Select(entry =>
            {
                var entryType = entry.GetType();
                var path = (string)entryType.GetProperty("Path")!.GetValue(entry)!;
                var offset = (int)entryType.GetProperty("Offset")!.GetValue(entry)!;
                var length = (int)entryType.GetProperty("Length")!.GetValue(entry)!;
                var contentType = (string)entryType.GetProperty("ContentType")!.GetValue(entry)!;
                var bytes = packedBytes.AsSpan(offset, length);
                var isJson = path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".gltf", StringComparison.Ordinal);
                var comparableBytes = ComparableBinary(path, bytes);
                return new
                {
                    path,
                    contentType,
                    offset,
                    length,
                    prefix = isJson ? null : Convert.ToBase64String(comparableBytes[..Math.Min(comparableBytes.Length, 64)]),
                    sha256 = isJson
                        ? null
                        : Convert.ToHexString(SHA256.HashData(comparableBytes)).ToLowerInvariant(),
                    json = isJson
                        ? NormalizeJson(JsonNode.Parse(bytes)!)
                        : null
                };
            })
            .ToArray();
        var gameplayEntry = entries.Select((entry, index) => (entry, index))
            .First(value => value.entry.path == "gameplay/gameplay_core.bin");
        var rawEntries = ((System.Collections.IEnumerable)type.GetProperty("Entries")!.GetValue(package)!).Cast<object>().ToArray();
        var rawGameplayEntry = rawEntries[gameplayEntry.index];
        var gameplayOffset = (int)rawGameplayEntry.GetType().GetProperty("Offset")!.GetValue(rawGameplayEntry)!;
        var gameplayLength = (int)rawGameplayEntry.GetType().GetProperty("Length")!.GetValue(rawGameplayEntry)!;
        var readGameplay = assembly.GetType("RatchetPs2.Games.UYA.Gameplay.UyaGameplayBlockReader")!
            .GetMethod("ReadCore", [typeof(ReadOnlySpan<byte>)])!
            .CreateDelegate<ReadGameplay>();
        var gameplay = readGameplay(packedBytes.AsSpan(gameplayOffset, gameplayLength));
        var gameplayJson = JsonSerializer.SerializeToNode(
            gameplay,
            gameplay.GetType(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            })!;
        StripGameplayBytes(gameplayJson);
        return new { entries, gameplay = gameplayJson };
    }

    private static void WritePackedBytes(string output, string name, object package) =>
        File.WriteAllBytes(Path.Combine(output, name),
            (byte[])package.GetType().GetProperty("PackedBytes")!.GetValue(package)!);

    private static byte[] ComparableBinary(string path, ReadOnlySpan<byte> bytes)
    {
        var result = bytes.ToArray();
        if (path.EndsWith(".buffer.bin", StringComparison.Ordinal))
            for (var offset = 0; offset + 4 <= result.Length; offset += 4)
                if (BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(offset, 4)) == 0x80000000)
                    result[offset + 3] = 0;
        return result;
    }

    private static JsonNode NormalizeJson(JsonNode node)
    {
        if (node is JsonObject value)
        {
            value.Remove("PerformanceTimings");
            value.Remove("performanceTimings");
            foreach (var child in value.ToArray())
                if (child.Value is not null) NormalizeJson(child.Value);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child is not null) NormalizeJson(child);
        }
        return node;
    }

    private static void StripGameplayBytes(JsonNode node)
    {
        if (node is JsonObject value)
        {
            foreach (var name in new[]
            {
                "headerBytes", "payloadBytes", "mobyLinksBytes", "tableBytes",
                "dataBytes", "relativePointerBytes", "data"
            }) value.Remove(name);
            foreach (var child in value.ToArray())
                if (child.Value is not null) StripGameplayBytes(child.Value);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child is not null) StripGameplayBytes(child);
        }
    }

    private static byte[] CreateZip(params (string Name, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in entries)
                using (var stream = archive.CreateEntry(name).Open()) stream.Write(bytes);
        return output.ToArray();
    }

    private static void Inflate(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        zlib.CopyTo(Stream.Null);
    }

    private static string? ZipError(Assembly assembly, byte[] bytes) => Error(() =>
        assembly.GetType("RatchetPs2.Games.UYA.Level.UyaCustomMapZipUnpacker")!
            .GetMethod("Unpack", [typeof(byte[])])!.Invoke(null, [bytes]));

    private static string? Error(Action action)
    {
        try { action(); return null; }
        catch (TargetInvocationException error) { return error.InnerException?.GetType().Name ?? error.GetType().Name; }
        catch (Exception error) { return error.GetType().Name; }
    }
}
