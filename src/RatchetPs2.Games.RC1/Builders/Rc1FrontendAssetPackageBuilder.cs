using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Games.RC1.Builders;

public static class Rc1FrontendAssetPackageBuilder
{
    public static PackedFilePackage Build(FrontendAssetKind kind, byte[] bytes,
        IReadOnlyList<FrontendAssetTexture> textures, bool preserveRepeatedTexCoordTiles = false) =>
        StandardFrontendAssetPackageBuilder.Build(GameId.RC1, kind, bytes, textures,
            preserveRepeatedTexCoordTiles, Rc1LevelAssetProfile.Default);
}
