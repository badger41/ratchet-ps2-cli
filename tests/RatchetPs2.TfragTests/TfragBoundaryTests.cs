using System.Buffers.Binary;
using System.Security.Cryptography;
using RatchetPs2.Core.Tfrags;

internal static class TfragBoundaryTests
{
    public static void Run(string repoRoot)
    {
        ValidateSyntheticTerrainBoundaries();
        WriteUyaTerrainInventory(repoRoot);
    }

    private static void ValidateSyntheticTerrainBoundaries()
    {
        var bytes = CreateSyntheticTerrain();
        var terrain = TfragTerrainReader.Read(bytes);
        Expect(terrain.SourceBytes.Span.SequenceEqual(bytes), "no-edit tfrag reads should preserve every source byte");
        Expect(terrain.Chunks.Count == 2, "synthetic tfrag terrain should contain two native groups");
        var reread = TfragTerrainReader.Read(terrain.SourceBytes.Span);
        Expect(reread.TfragTableOffset == terrain.TfragTableOffset
               && reread.TfragCount == terrain.TfragCount
               && reread.TotalTfragCount == terrain.TotalTfragCount
               && reread.Chunks.Select(chunk => (chunk.Index, chunk.DataOffset, chunk.DataLength))
                   .SequenceEqual(terrain.Chunks.Select(chunk => (chunk.Index, chunk.DataOffset, chunk.DataLength))),
            "no-edit tfrag source bytes should re-read to the same native group inventory");

        var first = terrain.Chunks[0];
        var second = terrain.Chunks[1];
        Expect(first.Index != second.Index && first.RecordOffset != second.RecordOffset && first.DataOffset != second.DataOffset,
            "duplicate tfrag data occurrences should retain distinct source provenance");
        var firstHash = SHA256.HashData(terrain.SourceBytes.Span.Slice(first.DataOffset, first.DataLength));
        var secondHash = SHA256.HashData(terrain.SourceBytes.Span.Slice(second.DataOffset, second.DataLength));
        Expect(firstHash.SequenceEqual(secondHash),
            "synthetic duplicate tfrag occurrences should share a content-derived source-data identity");

        var insideTable = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(insideTable.AsSpan(0x40 + 0x10, 4), 0x40);
        ExpectInvalidData(() => TfragTerrainReader.Read(insideTable),
            "tfrag data offsets inside the group table must be rejected");

        var repeatedOffset = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(repeatedOffset.AsSpan(0x80 + 0x10, 4), 0x80);
        ExpectInvalidData(() => TfragTerrainReader.Read(repeatedOffset),
            "tfrag data offsets must be strictly increasing");

        var unalignedOffset = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(unalignedOffset.AsSpan(0x40 + 0x10, 4), 0x81);
        ExpectInvalidData(() => TfragTerrainReader.Read(unalignedOffset),
            "tfrag data offsets must retain native 0x10-byte alignment");

        var impossibleCount = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(impossibleCount.AsSpan(0x04, 4), int.MaxValue);
        ExpectInvalidData(() => TfragTerrainReader.Read(impossibleCount),
            "tfrag record counts must be bounded by the payload before allocation");
    }

    internal static byte[] CreateSyntheticTerrain()
    {
        const int tableOffset = 0x40;
        const int recordSize = 0x40;
        const int dataSize = 0x10;
        var bytes = new byte[tableOffset + recordSize * 2 + dataSize * 2];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x00, 4), tableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x04, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x08, 4), BitConverter.SingleToInt32Bits(32f));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x0C, 4), 2);

        for (var index = 0; index < 2; index++)
        {
            var recordOffset = tableOffset + index * recordSize;
            var dataOffset = tableOffset + recordSize * 2 + index * dataSize;
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(recordOffset + 0x10, 4),
                dataOffset - tableOffset);
            bytes.AsSpan(dataOffset, dataSize).Fill(0xA5);
        }

        return bytes;
    }

    private static void WriteUyaTerrainInventory(string repoRoot)
    {
        var sources = new List<(string Owner, string Path, bool IsChunkWad)>();
        var primaryRoot = Path.Combine(repoRoot, "test-assets", "tfrags", "UYA");
        if (Directory.Exists(primaryRoot))
        {
            foreach (var path in Directory.EnumerateFiles(primaryRoot, "terrain.bin", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                sources.Add(("primary", path, false));
            }
        }

        var extractionRoot = Path.Combine(repoRoot, "test-assets", "extractions_uya");
        if (Directory.Exists(extractionRoot))
        {
            foreach (var path in Directory.EnumerateFiles(extractionRoot, "chunk*.wad", SearchOption.AllDirectories)
                         .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "chunks")
                         .Where(path => !Path.GetFileName(path).Equals("chunk0.wad", StringComparison.OrdinalIgnoreCase))
                         .Order(StringComparer.Ordinal))
            {
                sources.Add(("chunk", path, true));
            }
        }

        if (sources.Count == 0)
        {
            Console.WriteLine("No local UYA tfrag inventory fixtures found; skipping corpus inventory.");
            return;
        }

        var payloadCountByOwner = new Dictionary<string, int>(StringComparer.Ordinal);
        var sourcePayloadHashes = new HashSet<string>(StringComparer.Ordinal);
        var sourceDataHashCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var payloadCount = 0;
        var groupCount = 0;
        var largestPayloadBytes = 0;
        var largestPayload = "";
        var largestPayloadGroupCount = 0;
        var largestGroupCountPayload = "";
        var largestGroupBytes = 0;
        var largestGroup = "";
        var maxTextureEntries = 0;
        var maxRgbaSize = 0;
        var maxVertexCount = 0;
        var maxTriangleCount = 0;
        foreach (var source in sources)
        {
            var sourceBytes = File.ReadAllBytes(source.Path);
            var payload = source.IsChunkWad
                ? TfragChunkWadReader.ReadTerrainPayload(sourceBytes)
                : sourceBytes;
            if (payload.Length == 0
                || !sourcePayloadHashes.Add(Convert.ToHexString(SHA256.HashData(payload))))
            {
                continue;
            }

            var terrain = TfragTerrainReader.Read(payload);
            payloadCount++;
            payloadCountByOwner[source.Owner] = payloadCountByOwner.GetValueOrDefault(source.Owner) + 1;
            groupCount += terrain.Chunks.Count;
            if (payload.Length > largestPayloadBytes)
            {
                largestPayloadBytes = payload.Length;
                largestPayload = Path.GetRelativePath(repoRoot, source.Path);
            }
            if (terrain.Chunks.Count > largestPayloadGroupCount)
            {
                largestPayloadGroupCount = terrain.Chunks.Count;
                largestGroupCountPayload = Path.GetRelativePath(repoRoot, source.Path);
            }

            foreach (var chunk in terrain.Chunks)
            {
                maxTextureEntries = Math.Max(maxTextureEntries, chunk.TextureCount);
                maxRgbaSize = Math.Max(maxRgbaSize, chunk.RgbaSize);
                maxVertexCount = Math.Max(maxVertexCount, chunk.VertexCount);
                maxTriangleCount = Math.Max(maxTriangleCount, chunk.TriangleCount);
                if (chunk.DataLength > largestGroupBytes)
                {
                    largestGroupBytes = chunk.DataLength;
                    largestGroup = $"{Path.GetRelativePath(repoRoot, source.Path)}#{chunk.Index}";
                }
                var hash = Convert.ToHexString(SHA256.HashData(
                    terrain.SourceBytes.Span.Slice(chunk.DataOffset, chunk.DataLength)));
                sourceDataHashCounts[hash] = sourceDataHashCounts.GetValueOrDefault(hash) + 1;
            }
        }

        var duplicateHashes = sourceDataHashCounts.Count(entry => entry.Value > 1);
        var duplicateOccurrences = sourceDataHashCounts.Where(entry => entry.Value > 1).Sum(entry => entry.Value);
        var observedPrimaryCount = 0;
        var largestObservedPrimaryGroupCount = 0;
        var largestObservedPrimary = "";
        if (Directory.Exists(extractionRoot))
        {
            foreach (var path in Directory.EnumerateFiles(extractionRoot, "tfrag.bin", SearchOption.AllDirectories)
                         .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "tfrag")
                         .Order(StringComparer.Ordinal))
            {
                var terrain = TfragTerrainReader.Read(File.ReadAllBytes(path));
                observedPrimaryCount++;
                if (terrain.Chunks.Count > largestObservedPrimaryGroupCount)
                {
                    largestObservedPrimaryGroupCount = terrain.Chunks.Count;
                    largestObservedPrimary = Path.GetRelativePath(repoRoot, path);
                }
            }
        }

        Console.WriteLine(
            $"UYA tfrag inventory: exact-payloads={payloadCount} " +
            $"(primary={payloadCountByOwner.GetValueOrDefault("primary")}, chunk={payloadCountByOwner.GetValueOrDefault("chunk")}), " +
            $"exact-groups={groupCount}, max-payload-bytes={largestPayloadBytes} ({largestPayload}), " +
            $"max-payload-groups={largestPayloadGroupCount} ({largestGroupCountPayload}), " +
            $"max-group-bytes={largestGroupBytes} ({largestGroup}), " +
            $"max-textures={maxTextureEntries}, max-rgba-size={maxRgbaSize}, " +
            $"max-vertices={maxVertexCount}, max-triangles={maxTriangleCount}, " +
            $"duplicate-source-data-hashes={duplicateHashes}/{duplicateOccurrences} occurrences; " +
            $"observed-primary-headers={observedPrimaryCount}, " +
            $"max-observed-primary-groups={largestObservedPrimaryGroupCount} ({largestObservedPrimary}).");
    }

    private static void ExpectInvalidData(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
