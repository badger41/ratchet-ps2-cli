using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Sdk;

public static class LevelAssetExtractor
{
    public static LevelAssetExtractionResult ExtractLevelWad(GameId gameId, byte[] levelWadBytes) => gameId switch
    {
        GameId.UYA => UyaLevelAssetExtractor.Extract(levelWadBytes),
        GameId.DL => DlLevelAssetExtractor.Extract(levelWadBytes),
        GameId.GC => GcLevelAssetExtractor.Extract(levelWadBytes),
        GameId.RC1 => Rc1LevelAssetExtractor.Extract(levelWadBytes),
        _ => throw new NotSupportedException($"Level asset extraction is not supported for {gameId}."),
    };

    /// <summary>Extracts assets from already unpacked level files. Currently supported for UYA.</summary>
    public static LevelAssetExtractionResult ExtractUnpacked(GameId gameId, IReadOnlyList<PackedFile> unpackedFiles) => gameId switch
    {
        GameId.UYA => UyaLevelAssetExtractor.Extract(unpackedFiles),
        GameId.GC => GcLevelAssetExtractor.Extract(unpackedFiles),
        GameId.RC1 => Rc1LevelAssetExtractor.Extract(unpackedFiles),
        _ => throw new NotSupportedException($"Unpacked level asset extraction is not supported for {gameId}."),
    };
}
