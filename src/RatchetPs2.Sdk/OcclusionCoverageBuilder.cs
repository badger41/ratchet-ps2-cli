using RatchetPs2.Core.Games;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Games.UYA.Occlusion;

namespace RatchetPs2.Sdk;

/// <summary>Evaluates coverage coordinates without generating masks or publishing a native grid.</summary>
public static class OcclusionCoverageBuilder
{
    public static IReadOnlyList<OcclusionCoverageCoordinate> ReadCoordinates(GameId gameId, ReadOnlySpan<byte> grid,
        CancellationToken token = default) => gameId switch
    {
        GameId.UYA => UyaOcclusionGridReader.Read(grid, UyaOcclusionCoverage.Profile.MaximumCells, token)
            .Octants.Select(c => new OcclusionCoverageCoordinate(c.X, c.Y, c.Z)).ToArray(),
        _ => throw new NotSupportedException($"Coverage coordinate reading does not support {gameId}."),
    };

    public static OcclusionCoverageProfile GetPreviewProfile(GameId gameId) => gameId switch
    {
        GameId.UYA => UyaOcclusionCoverage.Profile,
        _ => throw new NotSupportedException($"Shape coverage preview does not support {gameId}."),
    };

    public static OcclusionCoveragePreview PreviewShapes(GameId gameId,
        IReadOnlyList<OcclusionCoverageCoordinate> primary, IReadOnlyList<OcclusionCoverageShape> shapes,
        CancellationToken token = default) => gameId switch
    {
        GameId.UYA => UyaOcclusionCoverage.Preview(primary, shapes, token),
        _ => throw new NotSupportedException($"Shape coverage preview does not support {gameId}."),
    };

    public static OcclusionGraphCoverageProfile GetGraphPreviewProfile(GameId gameId) => gameId switch
    {
        GameId.UYA => UyaOcclusionGraphCoverage.Profile,
        _ => throw new NotSupportedException($"Graph coverage preview does not support {gameId}."),
    };

    public static OcclusionGraphCoveragePreview PreviewGraphs(GameId gameId,
        IReadOnlyList<OcclusionCoverageCoordinate> primary, IReadOnlyList<OcclusionCoverageShape> shapes,
        IReadOnlyList<OcclusionCoverageGraph> graphs, IReadOnlyList<OcclusionSurfaceTriangle> triangles,
        IReadOnlyList<OcclusionAlphaTexture> textures, CancellationToken token = default, Action<int, int>? progress = null,
        IReadOnlyList<OcclusionSurfaceTriangle>? stickyTriangles = null) => gameId switch
    {
        GameId.UYA => UyaOcclusionGraphCoverage.Preview(primary, shapes, graphs, triangles, textures, token, progress, stickyTriangles),
        _ => throw new NotSupportedException($"Graph coverage preview does not support {gameId}."),
    };

    public static OcclusionCoverageProfile GetBoxPreviewProfile(GameId gameId) => gameId switch
    {
        GameId.UYA => UyaOcclusionBoxCoverage.Profile,
        _ => throw new NotSupportedException($"Box coverage preview does not support {gameId}."),
    };

    public static OcclusionCoveragePreview PreviewBoxes(GameId gameId,
        IReadOnlyList<OcclusionCoverageCoordinate> primary, IReadOnlyList<OcclusionCoverageBox> boxes,
        CancellationToken token = default) => gameId switch
    {
        GameId.UYA => UyaOcclusionBoxCoverage.Preview(primary, boxes, token),
        _ => throw new NotSupportedException($"Box coverage preview does not support {gameId}."),
    };
}
