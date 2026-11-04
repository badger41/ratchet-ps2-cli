using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaFrontendAssetPackageBuilder
{
    public static PackedFilePackage Build(FrontendAssetKind kind, byte[] modelBytes,
        IReadOnlyList<FrontendAssetTexture> textures, bool preserveTieRepeatedTexCoordTiles = false) =>
        StandardFrontendAssetPackageBuilder.Build(GameId.UYA, kind, modelBytes, textures, preserveTieRepeatedTexCoordTiles);
}
