using System.Numerics;

namespace RatchetPs2.Games.UYA.Collision;

internal static class UyaInstancedCollisionGeometry
{
    private const long MaximumDeviationFaceTests = 25_000_000;
    private const int MaximumSubdivisionSpan64 = 16 * 64;

    public static float MeasureMaximumSourceVertexDeviation(
        IReadOnlyList<Vector3> sourcePositions,
        IReadOnlyList<int> sourceVertexIndices,
        IReadOnlyList<UyaCollisionSolidFace> faces,
        string sourceLabel,
        CancellationToken cancellationToken)
    {
        var testCount = checked((long)sourceVertexIndices.Count * faces.Count);
        if (testCount > MaximumDeviationFaceTests)
        {
            throw new InvalidDataException(
                $"{sourceLabel} deviation measurement requires {testCount} vertex-face tests, "
                + $"exceeding the {MaximumDeviationFaceTests} test limit.");
        }

        var maximumSquared = 0f;
        foreach (var sourceIndex in sourceVertexIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var point = sourcePositions[sourceIndex];
            var minimumSquared = float.PositiveInfinity;
            foreach (var face in faces)
            {
                minimumSquared = MathF.Min(minimumSquared,
                    PointTriangleDistanceSquared(point, face.A.Position, face.B.Position, face.C.Position));
                if (face.IsQuad)
                {
                    minimumSquared = MathF.Min(minimumSquared,
                        PointTriangleDistanceSquared(point, face.A.Position, face.C.Position, face.D.Position));
                }
            }
            maximumSquared = MathF.Max(maximumSquared, minimumSquared);
        }
        return MathF.Sqrt(maximumSquared);
    }

    public static UyaCollisionSolidFace[] SplitForNativeOctants(
        IReadOnlyList<UyaCollisionSolidFace> faces,
        int maximumFaces,
        CancellationToken cancellationToken)
    {
        if (faces.Count > maximumFaces)
            throw new InvalidDataException(
                $"Generated collision has {faces.Count} faces, exceeding its {maximumFaces} face recipe limit.");
        var pending = new Stack<UyaCollisionSolidFace>(faces.Reverse());
        var result = new List<UyaCollisionSolidFace>(faces.Count);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var face = pending.Pop();
            if (UyaCollisionWriter.CanFitNativeOctant(face) && FitsSubdivisionSpan(face))
            {
                if (result.Count >= maximumFaces)
                    throw new InvalidDataException(
                        $"Generated collision requires more than {maximumFaces} faces to fit native octants.");
                result.Add(face);
                continue;
            }
            if (result.Count + pending.Count + 2 > maximumFaces)
                throw new InvalidDataException(
                    $"Generated collision requires more than {maximumFaces} faces to fit native octants.");
            if (face.IsQuad)
            {
                pending.Push(new(face.Type, face.A, face.C, face.D, default, IsQuad: false));
                pending.Push(new(face.Type, face.A, face.B, face.C, default, IsQuad: false));
                continue;
            }

            var edge = LongestEdge(face);
            var midpoint = Midpoint(edge.A, edge.B);
            if (midpoint == edge.A || midpoint == edge.B)
                throw new InvalidDataException("Generated collision face cannot be subdivided for native octants.");
            pending.Push(new(face.Type, midpoint, edge.B, edge.C, default, IsQuad: false));
            pending.Push(new(face.Type, edge.A, midpoint, edge.C, default, IsQuad: false));
        }
        return result.Distinct().ToArray();
    }

    private static (UyaCollisionVertex A, UyaCollisionVertex B, UyaCollisionVertex C) LongestEdge(
        UyaCollisionSolidFace face)
    {
        var ab = DistanceSquared(face.A, face.B);
        var bc = DistanceSquared(face.B, face.C);
        var ca = DistanceSquared(face.C, face.A);
        if (ab >= bc && ab >= ca) return (face.A, face.B, face.C);
        if (bc >= ca) return (face.B, face.C, face.A);
        return (face.C, face.A, face.B);
    }

    private static bool FitsSubdivisionSpan(UyaCollisionSolidFace face)
    {
        var vertices = face.IsQuad
            ? new[] { face.A, face.B, face.C, face.D }
            : new[] { face.A, face.B, face.C };
        return (long)vertices.Max(vertex => vertex.X64) - vertices.Min(vertex => vertex.X64)
                <= MaximumSubdivisionSpan64
            && (long)vertices.Max(vertex => vertex.Y64) - vertices.Min(vertex => vertex.Y64)
                <= MaximumSubdivisionSpan64
            && (long)vertices.Max(vertex => vertex.Z64) - vertices.Min(vertex => vertex.Z64)
                <= MaximumSubdivisionSpan64;
    }

    private static double DistanceSquared(UyaCollisionVertex left, UyaCollisionVertex right)
    {
        var x = (double)right.X64 - left.X64;
        var y = (double)right.Y64 - left.Y64;
        var z = (double)right.Z64 - left.Z64;
        return x * x + y * y + z * z;
    }

    private static UyaCollisionVertex Midpoint(UyaCollisionVertex left, UyaCollisionVertex right) => new(
        RoundToFour(((long)left.X64 + right.X64) / 2d),
        RoundToFour(((long)left.Y64 + right.Y64) / 2d),
        checked((int)Math.Round(((long)left.Z64 + right.Z64) / 2d, MidpointRounding.AwayFromZero)));

    private static int RoundToFour(double value) => checked((int)Math.Round(
        value / 4, MidpointRounding.AwayFromZero) * 4);

    private static float PointTriangleDistanceSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = point - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return ap.LengthSquared();

        var bp = point - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return bp.LengthSquared();

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            var v = d1 / (d1 - d3);
            return Vector3.DistanceSquared(point, a + ab * v);
        }

        var cp = point - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return cp.LengthSquared();

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            var w = d2 / (d2 - d6);
            return Vector3.DistanceSquared(point, a + ac * w);
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            var w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return Vector3.DistanceSquared(point, b + (c - b) * w);
        }

        var denominator = 1f / (va + vb + vc);
        return Vector3.DistanceSquared(point, a + ab * (vb * denominator) + ac * (vc * denominator));
    }
}
