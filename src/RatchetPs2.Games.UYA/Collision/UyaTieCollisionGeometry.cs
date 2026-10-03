using System.Numerics;

namespace RatchetPs2.Games.UYA.Collision;

internal static class UyaTieCollisionGeometry
{
    private const long MaximumDeviationFaceTests = 25_000_000;

    public static float MeasureMaximumSourceVertexDeviation(
        IReadOnlyList<Vector3> sourcePositions,
        IReadOnlyList<int> sourceVertexIndices,
        IReadOnlyList<UyaCollisionSolidFace> faces,
        int lodIndex,
        CancellationToken cancellationToken)
    {
        var testCount = checked((long)sourceVertexIndices.Count * faces.Count);
        if (testCount > MaximumDeviationFaceTests)
        {
            throw new InvalidDataException(
                $"Tie LOD {lodIndex} deviation measurement requires {testCount} vertex-face tests, "
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
