using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class FrontendAssetPackageBuilder
{
    public static PackedFilePackage Build(
        GameId gameId,
        FrontendAssetKind kind,
        byte[] modelBytes,
        IReadOnlyList<FrontendAssetTexture> textures) => gameId switch
        {
            GameId.UYA => UyaFrontendAssetPackageBuilder.Build(kind, modelBytes, textures),
            _ => throw new NotSupportedException($"Frontend asset packages are not supported for {gameId}."),
        };
}
