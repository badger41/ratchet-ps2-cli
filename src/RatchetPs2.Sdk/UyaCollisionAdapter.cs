using RatchetPs2.Games.UYA.Collision;

namespace RatchetPs2.Sdk;

internal static class UyaCollisionAdapter
{
    public static CollisionInspection Inspect(byte[] collisionBytes)
    {
        var collision = UyaCollisionReader.Read(collisionBytes);
        return new(collision.SolidPieces.Select(piece => new CollisionPieceInfo(
                CollisionPieceKind.Solid,
                piece.SourceIndex,
                piece.Faces.Count,
                CountVertices(piece),
                piece.Faces.GroupBy(face => face.Type).OrderBy(group => group.Key)
                    .Select(group => new CollisionTypeCount(group.Key, group.Count())).ToArray()))
            .Concat(collision.PlayerBarriers.Select(piece => new CollisionPieceInfo(
                CollisionPieceKind.PlayerBarrier,
                piece.SourceIndex,
                piece.Triangles.Count,
                piece.Vertices.Count,
                [])))
            .ToArray());
    }

    public static CollisionAnalysis Analyze(byte[] collisionBytes) =>
        FromAnalysis(UyaCollisionWriter.Analyze(UyaCollisionReader.Read(collisionBytes)));

    public static UyaCollisionGltfExport ExportGltf(
        byte[] collisionBytes,
        string gltfFileName,
        string? bufferFileName,
        bool minify,
        CollisionGltfPalette? palette) => UyaCollisionGltfExporter.Export(
            collisionBytes,
            gltfFileName,
            new UyaCollisionGltfExportOptions
            {
                BufferFileName = bufferFileName,
                Minify = minify,
                Palette = palette is null
                    ? UyaCollisionGltfPalette.Generic
                    : new(
                        palette.CollisionTypeSrgbColors,
                        palette.SoundTypeSrgbColors,
                        palette.PlayerBarrierSrgbColor),
            });

    public static UyaCollisionPieceEdit[] ToEdits(IReadOnlyList<CollisionPieceEdit> edits) =>
        edits.Select(edit => new UyaCollisionPieceEdit(
            ToKind(edit.Kind),
            edit.SourcePieceIndex,
            ToTicks(edit.TranslationX, edit.Kind == CollisionPieceKind.Solid ? 16 : 64, "translation X"),
            ToTicks(edit.TranslationY, edit.Kind == CollisionPieceKind.Solid ? 16 : 64, "translation Y"),
            ToTicks(edit.TranslationZ, 64, "translation Z"),
            edit.Remove)).ToArray();

    public static UyaCollisionSolidAddition ToAddition(CollisionSolidAddition addition)
    {
        ArgumentNullException.ThrowIfNull(addition);
        ArgumentNullException.ThrowIfNull(addition.Faces);
        return new(addition.Id, addition.Faces.Select(face => new UyaCollisionSolidFace(
            face.RawType,
            ToVertex(face.A, addition.Id),
            ToVertex(face.B, addition.Id),
            ToVertex(face.C, addition.Id),
            face.IsQuad ? ToVertex(face.D, addition.Id) : default,
            face.IsQuad)).ToArray());
    }

    public static CollisionSolidAddition FromAddition(UyaCollisionSolidAddition addition) => new(
        addition.Id,
        addition.Faces.Select(FromFace).ToArray());

    public static CollisionSolidPiece FromPiece(UyaCollisionSolidPiece piece) => new(
        piece.SourceIndex,
        piece.Faces.Select(FromFace).ToArray());

    public static UyaCollisionInstanceTransform ToTransform(CollisionInstanceTransform transform) => new(
        new(transform.Position.X, transform.Position.Y, transform.Position.Z),
        new(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W),
        new(transform.Scale.X, transform.Scale.Y, transform.Scale.Z));

    public static CollisionAnalysis FromAnalysis(UyaCollisionAnalysis analysis) => new(
        analysis.LogicalFaceCount,
        analysis.LogicalVertexCount,
        analysis.OccupiedOctantCount,
        analysis.DuplicateFaceCount,
        analysis.HardViolationCount,
        analysis.Octants.Select(octant => new CollisionOctantCost(
            octant.X,
            octant.Y,
            octant.Z,
            octant.FaceCount,
            octant.VertexCount,
            octant.QuadCount,
            octant.EncodedByteCount,
            octant.AdditionIds,
            octant.Violations)).ToArray());

    public static InstancedCollisionCandidate FromCandidate(UyaInstancedCollisionSurfaceCandidate candidate) => new(
        new(InstancedCollisionCandidateKind.Surface, candidate.GeneratorVersion,
            candidate.LodIndex, candidate.RawType, candidate.MaximumFaces),
        FromAddition(candidate.Addition),
        candidate.SourceVertexCount,
        candidate.SourceTriangleCount,
        candidate.GeneratedVertexCount,
        candidate.GeneratedFaceCount,
        candidate.MergedQuadCount,
        candidate.RemovedDegenerateFaceCount,
        candidate.RemovedDuplicateFaceCount,
        candidate.MaximumVertexDeviation,
        FromAnalysis(candidate.Analysis));

    public static InstancedCollisionCandidate FromCandidate(UyaInstancedCollisionHullCandidate candidate) => new(
        new(InstancedCollisionCandidateKind.ConvexHull, candidate.GeneratorVersion,
            candidate.LodIndex, candidate.RawType, candidate.MaximumFaces, candidate.ProfileSections),
        FromAddition(candidate.Addition),
        candidate.SourceVertexCount,
        candidate.SourceTriangleCount,
        candidate.GeneratedVertexCount,
        candidate.GeneratedFaceCount,
        candidate.MergedQuadCount,
        RemovedDegenerateFaceCount: 0,
        RemovedDuplicateFaceCount: 0,
        candidate.MaximumSourceVertexDeviation,
        FromAnalysis(candidate.Analysis));

    public static CollisionComposition FromComposition(UyaCollisionComposition composed) => new(
        composed.Bytes,
        composed.Changed,
        composed.EffectiveEdits.Select(edit => new CollisionPieceEdit(
            FromKind(edit.Kind),
            edit.SourcePieceIndex,
            edit.TranslationX64 / 64f,
            edit.TranslationY64 / 64f,
            edit.TranslationZ64 / 64f,
            edit.Remove)).ToArray(),
        composed.Analysis is null ? null : FromAnalysis(composed.Analysis));

    private static UyaCollisionVertex ToVertex(CollisionVertex vertex, string additionId) => new(
        ToTicks(vertex.X, 16, $"addition '{additionId}' vertex X"),
        ToTicks(vertex.Y, 16, $"addition '{additionId}' vertex Y"),
        ToTicks(vertex.Z, 64, $"addition '{additionId}' vertex Z"));

    private static CollisionVertex FromVertex(UyaCollisionVertex vertex) =>
        new(vertex.X64 / 64f, vertex.Y64 / 64f, vertex.Z64 / 64f);

    private static CollisionSolidFace FromFace(UyaCollisionSolidFace face) => new(
        face.Type,
        FromVertex(face.A),
        FromVertex(face.B),
        FromVertex(face.C),
        face.IsQuad ? FromVertex(face.D) : default,
        face.IsQuad);

    private static int CountVertices(UyaCollisionSolidPiece piece)
    {
        var vertices = new HashSet<UyaCollisionVertex>();
        foreach (var face in piece.Faces)
        {
            vertices.Add(face.A);
            vertices.Add(face.B);
            vertices.Add(face.C);
            if (face.IsQuad) vertices.Add(face.D);
        }
        return vertices.Count;
    }

    private static int ToTicks(float value, int precision, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), $"Collision {name} must be finite.");
        return checked((int)MathF.Round(value * precision) * (64 / precision));
    }

    private static UyaCollisionPieceKind ToKind(CollisionPieceKind kind) => kind switch
    {
        CollisionPieceKind.Solid => UyaCollisionPieceKind.Solid,
        CollisionPieceKind.PlayerBarrier => UyaCollisionPieceKind.PlayerBarrier,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown collision piece kind."),
    };

    private static CollisionPieceKind FromKind(UyaCollisionPieceKind kind) => kind switch
    {
        UyaCollisionPieceKind.Solid => CollisionPieceKind.Solid,
        UyaCollisionPieceKind.PlayerBarrier => CollisionPieceKind.PlayerBarrier,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown UYA collision piece kind."),
    };
}
