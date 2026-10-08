using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.UYA.Collision;

public static class UyaCollisionLinkRecovery
{
    private const int MaximumInstances = 100_000;
    private const float MinimumContainment = 0.95f;
    private const float TieMinimumAxisCoverage = 0.5f;
    // Retail shrub collision often covers only the trunk inside a much wider foliage mesh.
    private const float ShrubMinimumAxisCoverage = 0.15f;

    public static IReadOnlyList<UyaCollisionPieceCandidate> FindCandidates(
        byte[] collisionBytes,
        IReadOnlyList<UyaTieCollisionGroup> groups,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        ArgumentNullException.ThrowIfNull(groups);
        var collision = UyaCollisionReader.Read(collisionBytes);
        var pieces = PreparePieces(collision, cancellationToken);
        var result = new List<UyaCollisionPieceCandidate>();
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group is null)
                throw new ArgumentException("UYA TIE collision recovery groups cannot contain null values.", nameof(groups));
            if (group.TieBytes is null
                || group.TieBytes.Length == 0
                || group.TieBytes.Length > UyaInstancedCollisionSurfaceGenerator.MaximumSourceBytes)
                throw new ArgumentException(
                    $"UYA TIE input must contain 1 through {UyaInstancedCollisionSurfaceGenerator.MaximumSourceBytes} bytes.",
                    nameof(groups));
            if (group.Instances is null)
                throw new ArgumentException("UYA TIE collision recovery instances cannot be null.", nameof(groups));
            result.AddRange(FindCandidates(
                TieClassReader.Read(
                    group.TieBytes,
                    TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA))),
                pieces,
                group.Instances,
                cancellationToken));
        }
        return result;
    }

    public static IReadOnlyList<UyaCollisionPieceCandidate> FindShrubCandidates(
        byte[] collisionBytes,
        IReadOnlyList<UyaShrubCollisionGroup> groups,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        ArgumentNullException.ThrowIfNull(groups);
        var pieces = PreparePieces(UyaCollisionReader.Read(collisionBytes), cancellationToken);
        var result = new List<UyaCollisionPieceCandidate>();
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group is null || group.ShrubBytes is null
                || group.ShrubBytes.Length == 0
                || group.ShrubBytes.Length > UyaInstancedCollisionSurfaceGenerator.MaximumSourceBytes
                || group.Instances is null)
                throw new ArgumentException("UYA shrub collision recovery groups are invalid.", nameof(groups));
            var mesh = UyaShrubCollisionGenerator.ReadMesh(group.ShrubBytes, cancellationToken);
            result.AddRange(FindCandidates(
                mesh.Positions, pieces, group.Instances, "shrub", ShrubMinimumAxisCoverage, cancellationToken));
        }
        return result;
    }

    public static IReadOnlyList<UyaCollisionPieceCandidate> FindCandidates(
        TieClass tie,
        UyaMapCollision collision,
        IReadOnlyList<UyaCollisionInstance> instances,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(instances);
        return FindCandidates(tie, PreparePieces(collision, cancellationToken), instances, cancellationToken);
    }

    public static IReadOnlyList<UyaCollisionPieceCandidate> FindShrubCandidates(
        ShrubClass shrub,
        UyaMapCollision collision,
        IReadOnlyList<UyaCollisionInstance> instances,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shrub);
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(instances);
        var positions = ShrubSurfaceMeshExtractor.Extract(shrub).Positions;
        return FindCandidates(
            positions, PreparePieces(collision, cancellationToken), instances,
            "shrub", ShrubMinimumAxisCoverage, cancellationToken);
    }

    private static IReadOnlyList<UyaCollisionPieceCandidate> FindCandidates(
        TieClass tie,
        IReadOnlyList<PreparedPiece> pieces,
        IReadOnlyList<UyaCollisionInstance> instances,
        CancellationToken cancellationToken)
    {
        var lod = tie.LodTopologies
            .Where(value => value.TriangleCount > 0 && value.UnresolvedLogicalVertexCount == 0)
            .OrderByDescending(value => value.LodIndex)
            .FirstOrDefault()
            ?? throw new InvalidDataException("TIE contains no usable decoded surface LOD.");
        var positions = TieSurfaceMeshExtractor.Extract(tie, lod.LodIndex).Positions;
        return FindCandidates(positions, pieces, instances, "TIE", TieMinimumAxisCoverage, cancellationToken);
    }

    private static IReadOnlyList<UyaCollisionPieceCandidate> FindCandidates(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<PreparedPiece> pieces,
        IReadOnlyList<UyaCollisionInstance> instances,
        string sourceKind,
        float minimumAxisCoverage,
        CancellationToken cancellationToken)
    {
        if (instances.Count > MaximumInstances)
            throw new ArgumentException($"UYA {sourceKind} collision recovery exceeds {MaximumInstances} instances.", nameof(instances));
        if (instances.Any(value => value is null || string.IsNullOrWhiteSpace(value.Id))
            || instances.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != instances.Count)
            throw new ArgumentException($"UYA {sourceKind} collision recovery instance IDs must be unique and non-empty.", nameof(instances));
        if (positions.Count == 0) return [];
        var sourceMin = positions.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min);
        var sourceMax = positions.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max);
        var sourceSize = sourceMax - sourceMin;
        var padding = MathF.Max(0.25f, MathF.Max(sourceSize.X, MathF.Max(sourceSize.Y, sourceSize.Z)) * 0.05f);
        var expandedMin = sourceMin - new Vector3(padding);
        var expandedMax = sourceMax + new Vector3(padding);
        var sourceCenter = (sourceMin + sourceMax) / 2;
        var sourceRadius = MathF.Max(sourceSize.Length() / 2, 0.001f);
        var result = new List<UyaCollisionPieceCandidate>();

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transform = Matrix(instance.Transform);
            if (!Matrix4x4.Invert(transform, out var inverse)) continue;
            TransformBounds(expandedMin, expandedMax, transform, out var worldMin, out var worldMax);
            foreach (var piece in pieces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (piece.Vertices.Length == 0 || !Overlaps(piece.Minimum, piece.Maximum, worldMin, worldMax)) continue;
                var containedCount = 0;
                var pieceMin = new Vector3(float.PositiveInfinity);
                var pieceMax = new Vector3(float.NegativeInfinity);
                foreach (var vertex in piece.Vertices)
                {
                    var local = Vector3.Transform(vertex, inverse);
                    if (Inside(local, expandedMin, expandedMax)) containedCount++;
                    pieceMin = Vector3.Min(pieceMin, local);
                    pieceMax = Vector3.Max(pieceMax, local);
                }
                var contained = containedCount / (float)piece.Vertices.Length;
                if (contained < MinimumContainment) continue;
                var center = (pieceMin + pieceMax) / 2;
                if (!Inside(center, expandedMin, expandedMax)) continue;
                var pieceSize = pieceMax - pieceMin;
                var coverageX = AxisCoverage(pieceSize.X, sourceSize.X);
                var coverageY = AxisCoverage(pieceSize.Y, sourceSize.Y);
                var coverageZ = AxisCoverage(pieceSize.Z, sourceSize.Z);
                if ((coverageX >= minimumAxisCoverage ? 1 : 0)
                    + (coverageY >= minimumAxisCoverage ? 1 : 0)
                    + (coverageZ >= minimumAxisCoverage ? 1 : 0) < 2) continue;
                var topCoverage = (coverageX + coverageY + coverageZ
                    - MathF.Min(coverageX, MathF.Min(coverageY, coverageZ))) / 2;
                var centerFit = 1 - Math.Clamp(Vector3.Distance(center, sourceCenter) / sourceRadius, 0, 1);
                result.Add(new(
                    instance.Id,
                    piece.SourceIndex,
                    contained * 0.45f + topCoverage * 0.35f + centerFit * 0.2f));
            }
        }
        return result.OrderBy(value => value.SourcePieceIndex)
            .ThenBy(value => value.InstanceId, StringComparer.Ordinal).ToArray();
    }

    private static PreparedPiece[] PreparePieces(UyaMapCollision collision, CancellationToken cancellationToken) =>
        collision.SolidPieces.Select(piece =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vertices = new HashSet<Vector3>();
            foreach (var face in piece.Faces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                vertices.Add(face.A.Position);
                vertices.Add(face.B.Position);
                vertices.Add(face.C.Position);
                if (face.IsQuad) vertices.Add(face.D.Position);
            }
            var values = vertices.ToArray();
            return new PreparedPiece(
                piece.SourceIndex,
                values,
                values.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min),
                values.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max));
        }).ToArray();

    private static void TransformBounds(
        Vector3 minimum,
        Vector3 maximum,
        Matrix4x4 transform,
        out Vector3 transformedMinimum,
        out Vector3 transformedMaximum)
    {
        transformedMinimum = new(float.PositiveInfinity);
        transformedMaximum = new(float.NegativeInfinity);
        for (var corner = 0; corner < 8; corner++)
        {
            var value = Vector3.Transform(new(
                (corner & 1) == 0 ? minimum.X : maximum.X,
                (corner & 2) == 0 ? minimum.Y : maximum.Y,
                (corner & 4) == 0 ? minimum.Z : maximum.Z), transform);
            transformedMinimum = Vector3.Min(transformedMinimum, value);
            transformedMaximum = Vector3.Max(transformedMaximum, value);
        }
    }

    private static bool Overlaps(Vector3 minimum, Vector3 maximum, Vector3 otherMinimum, Vector3 otherMaximum) =>
        minimum.X <= otherMaximum.X && maximum.X >= otherMinimum.X
        && minimum.Y <= otherMaximum.Y && maximum.Y >= otherMinimum.Y
        && minimum.Z <= otherMaximum.Z && maximum.Z >= otherMinimum.Z;

    private static Matrix4x4 Matrix(UyaCollisionInstanceTransform transform)
    {
        if (!Finite(transform.Position) || !Finite(transform.Rotation) || !Finite(transform.Scale)
            || transform.Rotation.LengthSquared() == 0
            || transform.Scale.X == 0 || transform.Scale.Y == 0 || transform.Scale.Z == 0)
            throw new InvalidDataException("UYA instanced collision recovery transform is invalid.");
        return Matrix4x4.CreateScale(transform.Scale)
            * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(transform.Rotation))
            * Matrix4x4.CreateTranslation(transform.Position);
    }

    private static bool Inside(Vector3 value, Vector3 minimum, Vector3 maximum) =>
        value.X >= minimum.X && value.X <= maximum.X
        && value.Y >= minimum.Y && value.Y <= maximum.Y
        && value.Z >= minimum.Z && value.Z <= maximum.Z;

    private static float AxisCoverage(float piece, float source) =>
        source <= 0.001f ? piece <= 0.5f ? 1 : 0 : Math.Clamp(piece / source, 0, 1);

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private sealed record PreparedPiece(
        int SourceIndex,
        Vector3[] Vertices,
        Vector3 Minimum,
        Vector3 Maximum);
}
