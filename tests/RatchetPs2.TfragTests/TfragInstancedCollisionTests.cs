using RatchetPs2.Core.Games;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Sdk;

internal static class TfragInstancedCollisionTests
{
    public static void Run()
    {
        var canonical = TfragPieceCanonicalCodec.Write(Geometry());
        var lods = CollisionWork.InspectTfragSurfaceLodIndices(canonical, GameId.UYA);
        Expect(lods.SequenceEqual([0, 1, 2]), "tfrag collision inspection should expose populated LODs");

        var automatic = CollisionWork.GenerateTfragDecimatedCandidate(
            canonical, GameId.UYA, "tfrag:auto", rawType: 0x3d);
        Equal(0, automatic.Recipe.LodIndex, "automatic tfrag collision should preserve the highest-detail surface");
        Equal(0.5f, automatic.Recipe.TargetFaceRatio,
            "automatic tfrag decimation should preserve its requested target ratio");
        Equal((byte)0x3d, automatic.Recipe.RawType, "tfrag collision should preserve its raw type");
        Expect(automatic.GeneratedFaceCount > 0,
            "tfrag decimation should not empty a surface that cannot be safely reduced");
        Expect(automatic.Addition.Faces.All(face => !face.IsQuad),
            "tfrag decimation should retain triangle topology");

        var high = CollisionWork.GenerateTfragSurfaceCandidate(
            canonical, GameId.UYA, "tfrag:high", lodIndex: 0, rawType: 0x0f);
        Equal(0, high.Recipe.LodIndex, "explicit tfrag collision should retain the selected LOD");
        Expect(high.Addition.Faces.Count > 0, "explicit tfrag surface collision should contain faces");

        var coplanar = CollisionWork.GenerateTfragSurfaceCandidate(
            TfragPieceCanonicalCodec.Write(CoplanarGeometry()),
            GameId.UYA,
            "tfrag:coplanar",
            lodIndex: 0);
        Equal(2, coplanar.GeneratedFaceCount,
            "tfrag collision should preserve authored triangles instead of merging them into a quad");
        Equal(0, coplanar.MergedQuadCount,
            "tfrag collision should not introduce a diagonal-dependent native quad");
        Expect(coplanar.Addition.Faces.All(face => !face.IsQuad),
            "tfrag collision should emit only topology-preserving triangles");
        var decimatedCoplanar = CollisionWork.GenerateTfragDecimatedCandidate(
            TfragPieceCanonicalCodec.Write(DecimationGeometry()),
            GameId.UYA,
            "tfrag:coplanar-decimated",
            targetFaceRatio: 0.5f);
        Equal(2, decimatedCoplanar.GeneratedFaceCount,
            "tfrag decimation should reduce a safe coplanar surface to its requested target");

        var mixedWinding = CollisionWork.GenerateTfragSurfaceCandidate(
            TfragPieceCanonicalCodec.Write(MixedWindingGeometry()),
            GameId.UYA,
            "tfrag:mixed-winding");
        Expect(mixedWinding.Addition.Faces.All(face => CrossZ(face.A, face.B, face.C) < 0),
            "tfrag collision should orient every native face consistently from stored light normals");

        var hull = CollisionWork.GenerateTfragConvexHullCandidate(
            canonical, GameId.UYA, "tfrag:hull", lodIndex: 0, rawType: 0x0f, profileSections: 2);
        Equal(InstancedCollisionCandidateKind.ConvexHull, hull.Recipe.Kind,
            "tfrag collision should support the existing shrinkwrap method");
        Expect(hull.Addition.Faces.Count > 0, "tfrag shrinkwrap collision should contain faces");

        var encoded = CollisionWork.EncodeStandalone(GameId.UYA, [automatic.Addition]);
        var decoded = CollisionWork.DecodeSolidAddition(encoded.Bytes, GameId.UYA, "tfrag:decoded");
        Equal(automatic.Addition.Faces.Count, decoded.Faces.Count,
            "generated tfrag collision should survive native encode and decode");
    }

    private static TfragPieceGeometry Geometry()
    {
        var vertices = new[]
        {
            Vertex(0, 0, 0),
            Vertex(4, 0, 0),
            Vertex(0, 4, 0),
            Vertex(0, 0, 4),
        };
        TfragPieceLod Lod(int index) => new(index,
        [
            new(new(0, false, false), vertices, [0u, 1u, 2u, 0u, 3u, 1u]),
        ]);
        return new(
            new(new(0, 0, 0), new(4, 4, 4), 7),
            [0],
            [Lod(0), Lod(1), Lod(2)]);
    }

    private static TfragPieceGeometry MixedWindingGeometry()
    {
        var vertices = new[]
        {
            Vertex(0, 0, 0),
            Vertex(1, 0, 0),
            Vertex(0, 1, 0),
            Vertex(3, 0, 0),
            Vertex(4, 0, 0),
            Vertex(3, 1, 0),
        };
        TfragPieceLod Lod(int index) => new(
            index,
            [new(new(0, false, false), vertices, [0u, 1u, 2u, 3u, 5u, 4u])]);
        return new(
            new(new(0, 0, 0), new(4, 1, 0), 4),
            [0],
            [Lod(0), Lod(1), Lod(2)]);
    }

    private static TfragPieceGeometry CoplanarGeometry()
    {
        var vertices = new[]
        {
            Vertex(0, 0, 0),
            Vertex(4, 0, 0),
            Vertex(4, 4, 0),
            Vertex(0, 4, 0),
        };
        TfragPieceLod Lod(int index) => new(
            index,
            [new(new(0, false, false), vertices, [0u, 1u, 2u, 0u, 2u, 3u])]);
        return new(
            new(new(0, 0, 0), new(4, 4, 0), 6),
            [0],
            [Lod(0), Lod(1), Lod(2)]);
    }

    private static TfragPieceGeometry DecimationGeometry()
    {
        var vertices = new[]
        {
            Vertex(0, 0, 0),
            Vertex(2, 0, 0),
            Vertex(4, 0, 0),
            Vertex(4, 4, 0),
            Vertex(0, 4, 0),
        };
        TfragPieceLod Lod(int index) => new(
            index,
            [new(new(0, false, false), vertices, [0u, 1u, 4u, 1u, 2u, 4u, 2u, 3u, 4u])]);
        return new(
            new(new(0, 0, 0), new(4, 4, 0), 6),
            [0],
            [Lod(0), Lod(1), Lod(2)]);
    }

    private static float CrossZ(CollisionVertex a, CollisionVertex b, CollisionVertex c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static TfragPieceVertex Vertex(float x, float y, float z) => new(
        new(x, y, z),
        new(0, 0, 1),
        new(0, 0),
        new(1, 1, 1, 1),
        0,
        new(1, 1, 1, 1),
        new(0, 0, 1),
        1);

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
