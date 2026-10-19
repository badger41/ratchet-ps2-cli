using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Sdk;

public sealed record LevelAssetSourceFiles(
    byte[] HeaderBytes,
    byte[] PaletteBytes,
    byte[] AssetWadBytes,
    IReadOnlyDictionary<int, byte[]> ChunkWads);

public static class LevelArchiveReader
{
    public static byte[] ExtractPrimary(GameId gameId, Stream iso, int levelIndex) => gameId switch
    {
        GameId.UYA => UyaLooseLevelWadExtractor.ExtractPrimary(iso, levelIndex).Bytes,
        _ => throw new NotSupportedException($"Level archive extraction is not supported for {gameId}."),
    };

    public static LevelAssetSourceFiles ReadAssetSourceFiles(GameId gameId, byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);
        return gameId switch
        {
            GameId.UYA => ReadUyaAssetSourceFiles(levelWadBytes),
            _ => throw new NotSupportedException($"Level asset source reading is not supported for {gameId}."),
        };
    }

    private static LevelAssetSourceFiles ReadUyaAssetSourceFiles(byte[] levelWadBytes)
    {
        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        var source = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(package.Files);
        return new(source.HeaderBytes, source.PaletteBytes, source.AssetWadBytes, source.ChunkWads);
    }
}
