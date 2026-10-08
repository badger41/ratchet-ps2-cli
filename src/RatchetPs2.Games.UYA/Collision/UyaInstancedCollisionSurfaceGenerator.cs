using System.Numerics;

namespace RatchetPs2.Games.UYA.Collision;

internal static class UyaInstancedCollisionSurfaceGenerator
{
    internal const int SurfaceGeneratorVersion = 2;
    internal const int DefaultMaximumFaces = 1_000_000;

    internal const int MaximumSourceBytes = 64 * 1024 * 1024;
    private const int MaximumTriangles = DefaultMaximumFaces;

    internal static UyaInstancedCollisionSurfaceCandidate GenerateSurface(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<(int A, int B, int C)> triangles,
        string additionId,
        int lodIndex,
        string sourceLabel,
        byte rawType,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(triangles);
        ValidateAdditionId(additionId);
        if (maximumFaces < 1 || maximumFaces > DefaultMaximumFaces)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFaces),
                $"UYA collision maximum faces must be between 1 and {DefaultMaximumFaces}.");
        }
        if (triangles.Any(value => (uint)value.A >= positions.Count
            || (uint)value.B >= positions.Count || (uint)value.C >= positions.Count))
            throw new InvalidDataException($"{sourceLabel} contains an invalid triangle vertex index.");
        if (triangles.Count > MaximumTriangles)
        {
            throw new InvalidDataException(
                $"{sourceLabel} exceeds the {MaximumTriangles} triangle generation limit.");
        }

        var quantizedPositions = new UyaCollisionVertex[positions.Count];
        var maximumVertexDeviation = 0f;
        for (var index = 0; index < positions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var quantized = Quantize(positions[index]);
            quantizedPositions[index] = quantized;
            maximumVertexDeviation = MathF.Max(
                maximumVertexDeviation,
                Vector3.Distance(positions[index], quantized.Position));
        }

        var faces = new List<UyaCollisionSolidFace>(triangles.Count);
        var faceKeys = new HashSet<TriangleKey>();
        var removedDegenerate = 0;
        var removedDuplicate = 0;
        foreach (var triangle in triangles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = quantizedPositions[triangle.A];
            var b = quantizedPositions[triangle.B];
            var c = quantizedPositions[triangle.C];
            if (IsDegenerate(a, b, c))
            {
                removedDegenerate++;
                continue;
            }

            if (!faceKeys.Add(TriangleKey.Create(a, b, c)))
            {
                removedDuplicate++;
                continue;
            }

            faces.Add(new(rawType, a, b, c, default, IsQuad: false));
        }

        if (faces.Count == 0)
        {
            throw new InvalidDataException($"{sourceLabel} produced no usable collision faces.");
        }

        var (mergedFaces, mergedQuadCount) = MergeCoplanarPairs(faces, cancellationToken);
        // Native UYA collision blocks on the opposite side from render-mesh winding.
        var nativeFaces = UyaInstancedCollisionGeometry.SplitForNativeOctants(
            mergedFaces.Select(face => face.IsQuad
            ? new UyaCollisionSolidFace(face.Type, face.A, face.D, face.C, face.B, IsQuad: true)
            : new UyaCollisionSolidFace(face.Type, face.A, face.C, face.B, default, IsQuad: false))
            .ToArray(), maximumFaces, cancellationToken);

        var generatedVertices = nativeFaces
            .SelectMany(face => face.IsQuad
                ? new[] { face.A, face.B, face.C, face.D }
                : [face.A, face.B, face.C])
            .ToHashSet();
        var analysis = AnalyzeCandidate(additionId, nativeFaces, cancellationToken);
        return new(
            new(additionId, nativeFaces),
            SurfaceGeneratorVersion,
            lodIndex,
            rawType,
            maximumFaces,
            positions.Count,
            triangles.Count,
            generatedVertices.Count,
            nativeFaces.Length,
            nativeFaces.Count(face => face.IsQuad),
            removedDegenerate,
            removedDuplicate,
            maximumVertexDeviation,
            analysis);
    }

    internal static UyaCollisionAnalysis AnalyzeCandidate(
        string additionId,
        IReadOnlyList<UyaCollisionSolidFace> faces,
        CancellationToken cancellationToken) => UyaCollisionWriter.Analyze(
            new([new(0, faces)], [], 0, 0, 0),
            new Dictionary<int, string> { [0] = additionId },
            cancellationToken);

    private static (IReadOnlyList<UyaCollisionSolidFace> Faces, int MergedQuadCount) MergeCoplanarPairs(
        IReadOnlyList<UyaCollisionSolidFace> faces,
        CancellationToken cancellationToken)
    {
        var byEdge = new Dictionary<EdgeKey, List<int>>();
        for (var index = 0; index < faces.Count; index++)
        {
            foreach (var edge in Edges(faces[index]))
            {
                if (!byEdge.TryGetValue(edge, out var indexes))
                {
                    indexes = [];
                    byEdge.Add(edge, indexes);
                }

                indexes.Add(index);
            }
        }

        var consumed = new bool[faces.Count];
        var merged = new Dictionary<int, UyaCollisionSolidFace>();
        // ponytail: deterministic greedy pairs; use a matching optimizer only if corpus results justify it.
        foreach (var pair in byEdge
            .Where(pair => pair.Value.Count == 2)
            .OrderBy(pair => pair.Key.A.X64)
            .ThenBy(pair => pair.Key.A.Y64)
            .ThenBy(pair => pair.Key.A.Z64)
            .ThenBy(pair => pair.Key.B.X64)
            .ThenBy(pair => pair.Key.B.Y64)
            .ThenBy(pair => pair.Key.B.Z64))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leftIndex = pair.Value[0];
            var rightIndex = pair.Value[1];
            if (consumed[leftIndex] || consumed[rightIndex]
                || !TryMerge(faces[leftIndex], faces[rightIndex], pair.Key, out var quad))
            {
                continue;
            }

            consumed[leftIndex] = true;
            consumed[rightIndex] = true;
            merged.Add(Math.Min(leftIndex, rightIndex), quad);
        }

        var result = new List<UyaCollisionSolidFace>(faces.Count - merged.Count);
        for (var index = 0; index < faces.Count; index++)
        {
            if (merged.TryGetValue(index, out var quad)) result.Add(quad);
            else if (!consumed[index]) result.Add(faces[index]);
        }

        return (result, merged.Count);
    }

    private static bool TryMerge(
        UyaCollisionSolidFace left,
        UyaCollisionSolidFace right,
        EdgeKey shared,
        out UyaCollisionSolidFace quad)
    {
        quad = default;
        if (left.Type != right.Type
            || HasDirectedEdge(left, shared.A, shared.B) == HasDirectedEdge(right, shared.A, shared.B))
        {
            return false;
        }

        var next = new Dictionary<UyaCollisionVertex, UyaCollisionVertex>();
        foreach (var edge in DirectedEdges(left).Concat(DirectedEdges(right)))
        {
            if (EdgeKey.Create(edge.A, edge.B) == shared) continue;
            if (!next.TryAdd(edge.A, edge.B)) return false;
        }

        if (next.Count != 4
            || !next.TryGetValue(shared.A, out var b)
            || !next.TryGetValue(b, out var c)
            || c != shared.B
            || !next.TryGetValue(c, out var d)
            || !next.TryGetValue(d, out var end)
            || end != shared.A)
        {
            return false;
        }

        try
        {
            var normal = Cross(left.A, left.B, left.C);
            if (Dot(normal, Subtract(d, left.A)) != 0)
            {
                return false;
            }

            var vertices = new[] { shared.A, b, c, d };
            for (var index = 0; index < vertices.Length; index++)
            {
                var corner = Cross(
                    Subtract(vertices[(index + 1) % 4], vertices[index]),
                    Subtract(vertices[(index + 2) % 4], vertices[(index + 1) % 4]));
                if (Dot(corner, normal) <= 0) return false;
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        quad = new(left.Type, shared.A, b, c, d, IsQuad: true);
        return true;
    }

    private static bool HasDirectedEdge(
        UyaCollisionSolidFace face,
        UyaCollisionVertex a,
        UyaCollisionVertex b)
        => DirectedEdges(face).Any(edge => edge.A == a && edge.B == b);

    private static IEnumerable<EdgeKey> Edges(UyaCollisionSolidFace face)
        => DirectedEdges(face).Select(edge => EdgeKey.Create(edge.A, edge.B));

    private static IEnumerable<DirectedEdge> DirectedEdges(UyaCollisionSolidFace face)
    {
        yield return new(face.A, face.B);
        yield return new(face.B, face.C);
        yield return new(face.C, face.A);
    }

    private static IntegerVector Cross(
        UyaCollisionVertex a,
        UyaCollisionVertex b,
        UyaCollisionVertex c)
        => Cross(Subtract(b, a), Subtract(c, a));

    private static IntegerVector Cross(IntegerVector left, IntegerVector right) => new(
        checked(left.Y * right.Z - left.Z * right.Y),
        checked(left.Z * right.X - left.X * right.Z),
        checked(left.X * right.Y - left.Y * right.X));

    private static long Dot(IntegerVector left, IntegerVector right)
        => checked(left.X * right.X + left.Y * right.Y + left.Z * right.Z);

    private static IntegerVector Subtract(UyaCollisionVertex left, UyaCollisionVertex right)
        => new(
            (long)left.X64 - right.X64,
            (long)left.Y64 - right.Y64,
            (long)left.Z64 - right.Z64);

    internal static void ValidateAdditionId(string additionId)
    {
        if (string.IsNullOrWhiteSpace(additionId) || additionId.Length > 256)
        {
            throw new ArgumentException(
                "UYA collision addition ID must contain 1 through 256 non-whitespace characters.",
                nameof(additionId));
        }
    }

    internal static UyaCollisionVertex Quantize(Vector3 position) => new(
        ToTicks(position.X, 16, "X"),
        ToTicks(position.Y, 16, "Y"),
        ToTicks(position.Z, 64, "Z"));

    private static int ToTicks(float value, int precision, string axis)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException($"Collision source {axis} coordinate is not finite.");
        }

        return checked((int)MathF.Round(value * precision) * (64 / precision));
    }

    private static bool IsDegenerate(
        UyaCollisionVertex a,
        UyaCollisionVertex b,
        UyaCollisionVertex c)
    {
        var abx = (double)b.X64 - a.X64;
        var aby = (double)b.Y64 - a.Y64;
        var abz = (double)b.Z64 - a.Z64;
        var acx = (double)c.X64 - a.X64;
        var acy = (double)c.Y64 - a.Y64;
        var acz = (double)c.Z64 - a.Z64;
        return aby * acz - abz * acy == 0
            && abz * acx - abx * acz == 0
            && abx * acy - aby * acx == 0;
    }

    private static int Compare(UyaCollisionVertex left, UyaCollisionVertex right)
    {
        var result = left.X64.CompareTo(right.X64);
        if (result != 0) return result;
        result = left.Y64.CompareTo(right.Y64);
        return result != 0 ? result : left.Z64.CompareTo(right.Z64);
    }

    private readonly record struct TriangleKey(
        UyaCollisionVertex A,
        UyaCollisionVertex B,
        UyaCollisionVertex C)
    {
        public static TriangleKey Create(
            UyaCollisionVertex a,
            UyaCollisionVertex b,
            UyaCollisionVertex c)
        {
            if (Compare(a, b) > 0) (a, b) = (b, a);
            if (Compare(b, c) > 0) (b, c) = (c, b);
            if (Compare(a, b) > 0) (a, b) = (b, a);
            return new(a, b, c);
        }
    }

    private readonly record struct EdgeKey(UyaCollisionVertex A, UyaCollisionVertex B)
    {
        public static EdgeKey Create(UyaCollisionVertex a, UyaCollisionVertex b)
            => Compare(a, b) <= 0 ? new(a, b) : new(b, a);
    }

    private readonly record struct DirectedEdge(UyaCollisionVertex A, UyaCollisionVertex B);

    private readonly record struct IntegerVector(long X, long Y, long Z);
}
