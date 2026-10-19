using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Games.UYA.Textures;

namespace RatchetPs2.Sdk;

public static class StaticModelTextureConverter
{
    public static StaticModelTextureConversionResult Convert(
        GameId gameId,
        StaticModelCanonicalAsset asset,
        CancellationToken cancellationToken = default) => gameId switch
    {
        GameId.UYA => UyaStaticModelTextureConverter.Convert(asset, cancellationToken),
        _ => throw new NotSupportedException($"Static-model texture conversion is not supported for {gameId}."),
    };
}
