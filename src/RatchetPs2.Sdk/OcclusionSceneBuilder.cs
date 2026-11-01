using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Games.UYA.Occlusion;

namespace RatchetPs2.Sdk;

public static class OcclusionSceneBuilder
{
    public static OcclusionCoverageGeometry BuildCoverageGeometry(GameId gameId, ReadOnlySpan<byte> levelWad,
        CancellationToken token = default, bool includeStickyGeometry = false) => gameId switch
    {
        GameId.UYA => UyaOcclusionSceneBuilder.BuildCoverageGeometry(levelWad, token, includeStickyGeometry),
        _ => throw new NotSupportedException($"Coverage scene extraction does not support {gameId}."),
    };

    public static OcclusionSceneGeometry Build(GameId gameId, ReadOnlySpan<byte> levelWad,
        CancellationToken token = default) => gameId switch
    {
        GameId.UYA => UyaOcclusionSceneBuilder.Build(levelWad, token),
        _ => throw new NotSupportedException($"Occlusion scene extraction does not support {gameId}."),
    };

    public static IReadOnlyList<OcclusionReferenceMasks> TraceReference(GameId gameId,
        IReadOnlyList<OcclusionSurfaceTriangle> triangles, IReadOnlyList<(Vector3 Origin, Vector3 Direction)> rays,
        IReadOnlyList<OcclusionAlphaTexture> textures, IReadOnlyList<OcclusionTargetSphere> spheres,
        CancellationToken token = default, float backfaceExitDistance = 0, IReadOnlyList<float>? barrierHeights = null)
        => gameId switch
        {
            GameId.UYA => UyaOcclusionRayReference.TraceCalibrated(triangles, rays, textures, spheres, token,
                backfaceExitDistance, barrierHeights),
            _ => throw new NotSupportedException($"Occlusion reference tracing does not support {gameId}."),
        };

    public static OcclusionSampleQualifier CreateQualifier(GameId gameId, OcclusionSceneGeometry scene,
        CancellationToken token = default) => gameId switch
    {
        GameId.UYA => UyaOcclusionSceneBuilder.CreateQualifier(scene, token),
        _ => throw new NotSupportedException($"Occlusion qualification does not support {gameId}."),
    };
}
