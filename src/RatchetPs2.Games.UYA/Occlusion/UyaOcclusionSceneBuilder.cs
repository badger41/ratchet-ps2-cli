using RatchetPs2.Core.Occlusion;

namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionSceneBuilder
{
    public static OcclusionCoverageGeometry BuildCoverageGeometry(ReadOnlySpan<byte> wad, CancellationToken token = default, bool includeStickyGeometry = false)
    {
        var extracted = UyaOcclusionSceneExtractor.Extract(wad, token, coverageOnly: true);
        var (triangles, _) = PrepareGeometry(extracted, token);
        var stickySources = includeStickyGeometry ? extracted.Sources.Select((source, index) => (source, index))
            .Where(value => value.source.Family is "tie" or "tfrag").Select(value => value.index).ToHashSet() : null;
        OcclusionSurfaceTriangle[]? sticky = null;
        if (stickySources is not null)
            sticky = triangles.All(t => stickySources.Contains(t.SourceIndex)) ? triangles
                : triangles.Where(t => stickySources.Contains(t.SourceIndex)).ToArray();
        return new(triangles, extracted.Textures) { StickyTriangles = sticky };
    }

    public static OcclusionSceneGeometry Build(ReadOnlySpan<byte> wad, CancellationToken token = default)
    {
        var extracted = UyaOcclusionSceneExtractor.Extract(wad, token);
        var (triangles, fallback) = PrepareGeometry(extracted, token);
        var grid = UyaOcclusionGridReader.Read(extracted.Grid, cancellationToken: token, maximumOctants: 1_000_000);
        var octants = grid.Octants.OrderBy(cell => cell.Z).ThenBy(cell => cell.Y).ThenBy(cell => cell.X)
            .Select(cell => new OcclusionOctant(cell.X, cell.Y, cell.Z,
                (extracted.Grid[grid.MasksOffset + cell.MaskIndex * 128 + 127] & 128) != 0)).ToArray();
        return new(triangles, extracted.Textures, extracted.MobySpheres.Where(sphere => sphere.Group < 1023).ToArray(),
            octants, extracted.Grid, fallback, extracted.BarrierHeights);
    }

    private static (OcclusionSurfaceTriangle[] Triangles, byte[] Fallback) PrepareGeometry(
        UyaOcclusionExtractedScene extracted, CancellationToken token)
    {
        var ordered = UyaOcclusionTriangleOrder.Sort(extracted.Triangles, token);
        var (selected, fallback) = SelectBakeGeometry(ordered, extracted.Sources);
        return (selected.Select(triangle => triangle with { Flags = triangle.Flags | 2u }).ToArray(), fallback);
    }

    public static OcclusionSampleQualifier CreateQualifier(OcclusionSceneGeometry scene, CancellationToken token = default)
    {
        var blockers = scene.Triangles.Where(triangle => (triangle.Flags & 1) != 0).ToArray();
        UyaOcclusionAlphaSampling.Validate(blockers, scene.Textures);
        return new(blockers.Select(triangle => new OcclusionBlockingFace(triangle.A, triangle.B, triangle.C,
            (uint)(triangle.SourceIndex >= 0 ? triangle.SourceIndex + 1 : 1))).ToArray(),
            (face, u, v) => UyaOcclusionAlphaSampling.Sample(blockers[face], u, v, scene.Textures) >= 127, token);
    }

    public static (IReadOnlyList<OcclusionSurfaceTriangle> Triangles, byte[] Fallback) SelectBakeGeometry(IReadOnlyList<OcclusionSurfaceTriangle> triangles, IReadOnlyList<UyaOcclusionSource> sources)
    {
        var nonblocking = new HashSet<int>();
        var fallback = new byte[128];
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            // Treat Siege base/node variants conservatively; classes from GroupTemplates/dataset.json.
            var siege = source.Family == "tie" && source.ClassId is 0x02D5 or 0x02D6
                or >= 0x1D42 and <= 0x1D48 or >= 0x1D4C and <= 0x1D4E
                or 0x1F19 or 0x1F1F or >= 0x1F20 and <= 0x1F22;
            if (siege) nonblocking.Add(i);
            if (source.Family != "moby" && source.Fallback is not null && source.Group is >= 0 and < 1023)
                fallback[source.Group / 8] |= (byte)(1 << (source.Group % 8));
        }
        // Siege pieces remain visible targets without becoming blockers or overriding their native groups.
        return (triangles.Select(t => nonblocking.Contains(t.SourceIndex) ? t with { Flags = t.Flags & ~1u } : t).ToArray(), fallback);
    }

}
