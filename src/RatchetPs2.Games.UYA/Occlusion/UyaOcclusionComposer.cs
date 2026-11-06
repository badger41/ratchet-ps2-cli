using RatchetPs2.Core.Occlusion;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Builders;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionComposer
{
    public static byte[] ReadGrid(ReadOnlySpan<byte> levelWad, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(UyaLevelWadUnpacker.Unpack(levelWad).Files);
        return UyaOcclusionGridReader.ReadLevelAssetPayload(source.HeaderBytes, source.AssetWadBytes);
    }

    public static void ValidateGrid(ReadOnlySpan<byte> original, ReadOnlySpan<byte> generated, int count,
        CancellationToken token = default)
    {
        var before = UyaOcclusionGridReader.Read(original, 1_000_000, token);
        var after = UyaOcclusionGridReader.Read(generated, 1_000_000, token);
        if (before.Octants.Count != count || after.Octants.Count != count
            || !before.Octants.Select(cell => (cell.X, cell.Y, cell.Z))
                .SequenceEqual(after.Octants.Select(cell => (cell.X, cell.Y, cell.Z))))
            throw new InvalidDataException("Generated occlusion changes the native octant coordinates.");
    }

    public static byte[] RebuildGrid(ReadOnlySpan<byte> original,
        IReadOnlyDictionary<(int X, int Y, int Z), byte[]> masks, CancellationToken token = default)
        => UyaOcclusionMaskCompactor.Rebuild(original, masks, token);

    public static byte[] CompactGrid(ReadOnlySpan<byte> original, byte[] generated, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var source = UyaOcclusionGridReader.Read(original, 100_000, token);
        ValidateGrid(original, generated, source.Octants.Count, token);
        if (generated.Length <= original.Length) return generated;
        var grid = UyaOcclusionGridReader.Read(generated, 100_000, token);
        var masks = grid.Octants.ToDictionary(c => (c.X, c.Y, c.Z),
            c => generated.AsSpan(grid.MasksOffset + c.MaskIndex * 128, 128).ToArray());
        return RebuildGrid(original, masks, token);
    }

    public static LevelArchiveBuildResult ApplyToLevel(ReadOnlyMemory<byte> levelWad, byte[] generated, int octants,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(UyaLevelWadUnpacker.Unpack(levelWad.Span).Files);
        var original = UyaOcclusionGridReader.ReadLevelAssetPayload(source.HeaderBytes, source.AssetWadBytes);
        ValidateGrid(original, generated, octants, token);
        generated = CompactGrid(original, generated, token);
        return ApplyCore(levelWad, source, generated, token);
    }

    public static LevelArchiveBuildResult ApplyCoverageToLevel(ReadOnlyMemory<byte> levelWad,
        IReadOnlyDictionary<(int X, int Y, int Z), byte[]> masks, CancellationToken token = default)
    {
        var generated = UyaOcclusionGridWriter.Create(masks, token);
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(UyaLevelWadUnpacker.Unpack(levelWad.Span).Files);
        return ApplyCore(levelWad, source, generated, token);
    }

    public static LevelArchiveBuildResult ApplyCoverageToLevel(ReadOnlyMemory<byte> levelWad,
        byte[] generated, int octants, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(generated);
        var grid = UyaOcclusionGridReader.Read(generated, OcclusionSamplePlanner.MaximumCells, token);
        if (octants < 1 || grid.Octants.Count != octants)
            throw new InvalidDataException("Generated coverage has an invalid octant inventory.");
        var masks = grid.Octants.ToDictionary(c => (c.X, c.Y, c.Z),
            c => generated.AsSpan(grid.MasksOffset + c.MaskIndex * 128, 128).ToArray());
        return ApplyCoverageToLevel(levelWad, masks, token);
    }

    public static LevelArchiveBuildResult ClearCoverageFromLevel(ReadOnlyMemory<byte> levelWad,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(UyaLevelWadUnpacker.Unpack(levelWad.Span).Files);
        return ApplyCore(levelWad, source, UyaOcclusionGridWriter.CreateEmpty(token), token);
    }

    private static LevelArchiveBuildResult ApplyCore(ReadOnlyMemory<byte> levelWad,
        UyaLevelAssetSourceFiles source, byte[] generated, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var assets = BinaryMagic.IsWad(source.AssetWadBytes) ? WadCompression.Decompress(source.AssetWadBytes) : source.AssetWadBytes;
        var composed = UyaLevelAssetComposer.ComposeAssetWad(source.HeaderBytes, assets, new(Occlusion: generated), token);
        var built = UyaLevelArchiveBuilder.Build(levelWad, new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["assets/asset_header.bin"] = composed.HeaderBytes,
            ["assets/asset_wad_payload.bin"] = composed.AssetWadBytes,
        }, cancellationToken: token);
        if (!built.Succeeded || built.OutputBytes is null)
            throw new InvalidDataException("Packing rebuilt occlusion failed SDK validation.");
        if (!ReadGrid(built.OutputBytes, token).AsSpan().SequenceEqual(generated))
            throw new InvalidDataException("Packed occlusion failed native readback.");
        return built;
    }
}
