using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Builders;
using RatchetPs2.Games.DL.Builders;
using RatchetPs2.Games.GC.Builders;
using RatchetPs2.Games.RC1.Builders;

namespace RatchetPs2.Sdk;

public sealed record FrontendAssetPackageBuildOptions(
    bool PreserveAuthoredRepeatedTexCoordTiles = false);

public static class FrontendAssetPackageBuilder
{
    public static PackedFilePackage Build(
        GameId gameId,
        FrontendAssetKind kind,
        byte[] modelBytes,
        IReadOnlyList<FrontendAssetTexture> textures,
        FrontendAssetPackageBuildOptions? options = null) => gameId switch
        {
            GameId.RC1 => Rc1FrontendAssetPackageBuilder.Build(
                kind, modelBytes, textures, options?.PreserveAuthoredRepeatedTexCoordTiles ?? false),
            GameId.GC => GcFrontendAssetPackageBuilder.Build(
                kind, modelBytes, textures, options?.PreserveAuthoredRepeatedTexCoordTiles ?? false),
            GameId.UYA => UyaFrontendAssetPackageBuilder.Build(
                kind,
                modelBytes,
                textures,
                options?.PreserveAuthoredRepeatedTexCoordTiles ?? false),
            GameId.DL => DlFrontendAssetPackageBuilder.Build(
                kind,
                modelBytes,
                textures,
                options?.PreserveAuthoredRepeatedTexCoordTiles ?? false),
            _ => throw new NotSupportedException($"Frontend asset packages are not supported for {gameId}."),
        };
}
