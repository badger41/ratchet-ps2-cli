using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
using static RatchetPs2.Core.IO.BinarySpanReader;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad;

namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionGridReader
{
    public static UyaOcclusionGrid ReadLevelWad(ReadOnlySpan<byte> levelWadBytes)
    {
        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(package.Files);
        return ReadLevelAsset(source.HeaderBytes, source.AssetWadBytes);
    }

    public static UyaOcclusionGrid ReadLevelAsset(
        ReadOnlySpan<byte> assetHeaderBytes,
        ReadOnlySpan<byte> assetWadBytes)
    {
        var payload = ReadLevelAssetPayload(assetHeaderBytes, assetWadBytes);
        return payload.Length >= 8 ? Read(payload) : new(0, []);
    }

    public static byte[] ReadLevelAssetPayload(
        ReadOnlySpan<byte> assetHeaderBytes,
        ReadOnlySpan<byte> assetWadBytes)
    {
        var header = LevelAssetReader.ReadHeader(assetHeaderBytes);
        if (header.OcclusionOffset <= 0) return [];
        var assets = BinaryMagic.IsWad(assetWadBytes)
            ? WadCompression.Decompress(assetWadBytes)
            : assetWadBytes.ToArray();
        var mobys = LevelAssetReader.ReadModelDefinitions(
            assetHeaderBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = LevelAssetReader.ReadModelDefinitions(
            assetHeaderBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = LevelAssetReader.ReadShrubDefinitions(
            assetHeaderBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var offsets = LevelAssetReader.CollectKnownAssetOffsets(
            header, assets.Length, mobys, ties, shrubs, [header.SceneViewSize]);
        var payload = LevelAssetReader.ReadAssetSlice(assets, header.OcclusionOffset, offsets);
        if (payload.Length >= 8) _ = Read(payload);
        return payload;
    }

    public static UyaOcclusionGrid Read(ReadOnlySpan<byte> data) => Read(data, 1_000_000);

    public static UyaOcclusionGrid Read(
        ReadOnlySpan<byte> data,
        int maximumOctants,
        CancellationToken cancellationToken = default)
    {
        if (maximumOctants <= 0) throw new ArgumentOutOfRangeException(nameof(maximumOctants));
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRange(data, 0, 8, "UYA occlusion grid header");
        var masksOffset = ReadInt32LittleEndian(data, 0);
        if (masksOffset < 0) throw new InvalidDataException("UYA occlusion masks offset is negative.");
        var zStart = ReadUInt16LittleEndian(data, 4);
        var zCount = ReadUInt16LittleEndian(data, 6);
        EnsureRange(data, 8, zCount * sizeof(ushort), "UYA occlusion Z table");
        if (masksOffset > data.Length)
            throw new InvalidDataException("UYA occlusion masks point outside the grid.");

        var octants = new List<UyaOcclusionOctant>();
        long visitedEntries = 0;
        var maximumEntries = Math.Max(262_144L, maximumOctants * 8L);
        void CheckWork()
        {
            if (++visitedEntries > maximumEntries)
                throw new InvalidDataException("UYA occlusion tree exceeds the bounded table-entry work limit.");
            if ((visitedEntries & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
        }
        for (var z = 0; z < zCount; z++)
        {
            CheckWork();
            var zOffset = ReadUInt16LittleEndian(data, 8 + z * sizeof(ushort)) * 4;
            if (zOffset == 0) continue;
            EnsureRange(data, zOffset, 4, "UYA occlusion Y header");
            var yStart = ReadUInt16LittleEndian(data, zOffset);
            var yCount = ReadUInt16LittleEndian(data, zOffset + 2);
            EnsureRange(data, zOffset + 4, yCount * sizeof(ushort), "UYA occlusion Y table");
            for (var y = 0; y < yCount; y++)
            {
                CheckWork();
                var yOffset = ReadUInt16LittleEndian(data, zOffset + 4 + y * sizeof(ushort)) * 4;
                if (yOffset == 0) continue;
                EnsureRange(data, yOffset, 4, "UYA occlusion X header");
                var xStart = ReadUInt16LittleEndian(data, yOffset);
                var xCount = ReadUInt16LittleEndian(data, yOffset + 2);
                EnsureRange(data, yOffset + 4, xCount * sizeof(ushort), "UYA occlusion X table");
                for (var x = 0; x < xCount; x++)
                {
                    CheckWork();
                    var maskIndex = ReadUInt16LittleEndian(data, yOffset + 4 + x * sizeof(ushort));
                    if (maskIndex == ushort.MaxValue) continue;
                    EnsureRange(data, checked(masksOffset + maskIndex * 128), 128, "UYA occlusion visibility mask");
                    if (octants.Count >= maximumOctants)
                        throw new InvalidDataException($"UYA occlusion grid exceeds the {maximumOctants}-octant analysis limit.");
                    octants.Add(new(xStart + x, yStart + y, zStart + z, maskIndex));
                }
            }
        }
        return new(masksOffset, octants);
    }
}

public static class UyaOcclusionGridWriter
{
    private const int MaskSize = 128;

    public static byte[] ReplaceMasks(ReadOnlySpan<byte> data,
        IReadOnlyDictionary<(int X, int Y, int Z), byte[]> masks, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(masks);
        var grid = UyaOcclusionGridReader.Read(data, 1_000_000, token);
        if (masks.Count != grid.Octants.Count || grid.Octants.Count == 0
            || grid.Octants.Any(c => !masks.TryGetValue((c.X, c.Y, c.Z), out var mask) || mask is null || mask.Length != MaskSize))
            throw new InvalidDataException("Supply exactly one 128-byte mask per existing octant.");
        var slots = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var indices = new Dictionary<(int, int, int), ushort>(); var unique = new List<byte[]>();
        foreach (var c in grid.Octants)
        {
            token.ThrowIfCancellationRequested();
            var mask = masks[(c.X, c.Y, c.Z)]; var key = Convert.ToHexString(mask);
            if (!slots.TryGetValue(key, out var index))
            {
                if (unique.Count >= ushort.MaxValue) throw new InvalidDataException("Generated mask table exceeds native slot capacity.");
                index = (ushort)unique.Count; slots.Add(key, index); unique.Add(mask.ToArray());
            }
            indices.Add((c.X, c.Y, c.Z), index);
        }
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Align(int size) { while (stream.Position % size != 0) writer.Write((byte)0); }
        void Pointer(long at)
        {
            var end = stream.Position;
            if (end % 4 != 0 || end / 4 > ushort.MaxValue) throw new InvalidDataException("Generated occlusion tree exceeds native pointer capacity.");
            stream.Position = at; writer.Write((ushort)(end / 4)); stream.Position = end;
        }
        static (int Start, int Count) Range(IEnumerable<int> values)
        {
            var array = values.ToArray(); var start = Math.Min(array.Min(), ushort.MaxValue); var count = array.Max() - start + 1;
            if (count is < 1 or > ushort.MaxValue) throw new InvalidDataException("Generated tree span exceeds native coordinate capacity.");
            return (start, count);
        }
        var zRange = Range(grid.Octants.Select(c => c.Z));
        writer.Write(0); writer.Write((ushort)zRange.Start); writer.Write((ushort)zRange.Count);
        for (var i = 0; i < zRange.Count; i++) writer.Write((ushort)0);
        foreach (var z in grid.Octants.GroupBy(c => c.Z).OrderBy(g => g.Key))
        {
            token.ThrowIfCancellationRequested(); Align(4); Pointer(8 + (z.Key - zRange.Start) * 2L);
            var yRange = Range(z.Select(c => c.Y)); writer.Write((ushort)yRange.Start); writer.Write((ushort)yRange.Count);
            var yTable = stream.Position; for (var i = 0; i < yRange.Count; i++) writer.Write((ushort)0);
            foreach (var y in z.GroupBy(c => c.Y).OrderBy(g => g.Key))
            {
                token.ThrowIfCancellationRequested(); Align(4); Pointer(yTable + (y.Key - yRange.Start) * 2L);
                var row = y.ToDictionary(c => c.X); var xRange = Range(row.Keys);
                writer.Write((ushort)xRange.Start); writer.Write((ushort)xRange.Count);
                for (var x = xRange.Start; x < xRange.Start + xRange.Count; x++)
                    writer.Write(row.ContainsKey(x) ? indices[(x, y.Key, z.Key)] : ushort.MaxValue);
            }
        }
        Align(16); var masksOffset = checked((int)stream.Position); stream.Position = 0; writer.Write(masksOffset); stream.Position = masksOffset;
        foreach (var mask in unique) writer.Write(mask); Align(64); var output = stream.ToArray();
        var verified = UyaOcclusionGridReader.Read(output, 1_000_000, token);
        if (!verified.Octants.Select(c => (c.X, c.Y, c.Z)).SequenceEqual(grid.Octants.Select(c => (c.X, c.Y, c.Z)))
            || verified.Octants.Any(c => !output.AsSpan(verified.MasksOffset + c.MaskIndex * MaskSize, MaskSize).SequenceEqual(masks[(c.X, c.Y, c.Z)])))
            throw new InvalidDataException("Generated occlusion masks failed semantic readback.");
        return output;
    }

    public static byte[] SetAlwaysVisibleBit(ReadOnlySpan<byte> data, int bitIndex)
    {
        if (bitIndex is < 0 or >= MaskSize * 8)
            throw new ArgumentOutOfRangeException(nameof(bitIndex));
        var grid = UyaOcclusionGridReader.Read(data);
        if (grid.Octants.Count == 0)
            throw new InvalidDataException("UYA occlusion grid has no visibility masks.");
        var output = data.ToArray();
        foreach (var maskIndex in grid.Octants.Select(value => value.MaskIndex).Distinct())
        {
            var offset = checked(grid.MasksOffset + maskIndex * MaskSize + bitIndex / 8);
            output[offset] |= (byte)(1 << (bitIndex & 7));
        }
        return output;
    }
}

public sealed record UyaOcclusionGrid(int MasksOffset, IReadOnlyList<UyaOcclusionOctant> Octants);

public readonly record struct UyaOcclusionOctant(int X, int Y, int Z, int MaskIndex);
