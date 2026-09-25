using static RatchetPs2.Core.IO.BinarySpanReader;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.UYA.Level;

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
        var header = DlAssetReader.ReadHeader(assetHeaderBytes);
        if (header.OcclusionOffset <= 0) return new(0, []);
        var assets = BinaryMagic.IsWad(assetWadBytes)
            ? WadCompression.Decompress(assetWadBytes)
            : assetWadBytes.ToArray();
        var mobys = DlAssetReader.ReadModelDefinitions(
            assetHeaderBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = DlAssetReader.ReadModelDefinitions(
            assetHeaderBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = DlAssetReader.ReadShrubDefinitions(
            assetHeaderBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var offsets = DlAssetReader.CollectKnownAssetOffsets(
            GameId.UYA, header, assets.Length, mobys, ties, shrubs);
        var payload = DlAssetReader.ReadAssetSlice(assets, header.OcclusionOffset, offsets);
        return payload.Length >= 8 ? Read(payload) : new(0, []);
    }

    public static UyaOcclusionGrid Read(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, 8, "UYA occlusion grid header");
        var masksOffset = ReadInt32LittleEndian(data, 0);
        if (masksOffset < 0) throw new InvalidDataException("UYA occlusion masks offset is negative.");
        var zStart = ReadUInt16LittleEndian(data, 4);
        var zCount = ReadUInt16LittleEndian(data, 6);
        EnsureRange(data, 8, zCount * sizeof(ushort), "UYA occlusion Z table");
        if (masksOffset > data.Length)
            throw new InvalidDataException("UYA occlusion masks point outside the grid.");

        var octants = new List<UyaOcclusionOctant>();
        for (var z = 0; z < zCount; z++)
        {
            var zOffset = ReadUInt16LittleEndian(data, 8 + z * sizeof(ushort)) * 4;
            if (zOffset == 0) continue;
            EnsureRange(data, zOffset, 4, "UYA occlusion Y header");
            var yStart = ReadUInt16LittleEndian(data, zOffset);
            var yCount = ReadUInt16LittleEndian(data, zOffset + 2);
            EnsureRange(data, zOffset + 4, yCount * sizeof(ushort), "UYA occlusion Y table");
            for (var y = 0; y < yCount; y++)
            {
                var yOffset = ReadUInt16LittleEndian(data, zOffset + 4 + y * sizeof(ushort)) * 4;
                if (yOffset == 0) continue;
                EnsureRange(data, yOffset, 4, "UYA occlusion X header");
                var xStart = ReadUInt16LittleEndian(data, yOffset);
                var xCount = ReadUInt16LittleEndian(data, yOffset + 2);
                EnsureRange(data, yOffset + 4, xCount * sizeof(ushort), "UYA occlusion X table");
                for (var x = 0; x < xCount; x++)
                {
                    var maskIndex = ReadUInt16LittleEndian(data, yOffset + 4 + x * sizeof(ushort));
                    if (maskIndex == ushort.MaxValue) continue;
                    EnsureRange(data, checked(masksOffset + maskIndex * 128), 128, "UYA occlusion visibility mask");
                    octants.Add(new(xStart + x, yStart + y, zStart + z, maskIndex));
                }
            }
        }
        return new(masksOffset, octants);
    }
}

public sealed record UyaOcclusionGrid(int MasksOffset, IReadOnlyList<UyaOcclusionOctant> Octants);

public readonly record struct UyaOcclusionOctant(int X, int Y, int Z, int MaskIndex);
