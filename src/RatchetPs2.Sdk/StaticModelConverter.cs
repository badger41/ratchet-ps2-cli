using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Games.UYA.Ties;

namespace RatchetPs2.Sdk;

public static class StaticModelConverter
{
    public static StaticModelNativeConversionResult Convert(
        GameId gameId,
        StaticModelCanonicalAsset asset,
        int targetClassId,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaStaticTieComposer.Convert(asset, targetClassId, cancellationToken),
            _ => throw new NotSupportedException($"Static model conversion is not supported for {gameId}."),
        };
}
