namespace RatchetPs2.Games.UYA.Collision;

internal static class UyaInstancedCollisionHullGenerator
{
    internal const int GeneratorVersion = 4;
    internal const int DefaultProfileSections = 6;
    internal const int DefaultMaximumFaces = 100_000;
    internal const int MaximumProfileSections = 16;
    private const int MaximumRingVertices = 12;

    internal static UyaInstancedCollisionHullCandidate Generate(
        IReadOnlyList<System.Numerics.Vector3> positions,
        IReadOnlyList<(int A, int B, int C)> triangles,
        string additionId,
        int lodIndex,
        string sourceLabel,
        byte rawType = 0,
        int profileSections = DefaultProfileSections,
        int maximumFaces = DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(triangles);
        UyaInstancedCollisionSurfaceGenerator.ValidateAdditionId(additionId);
        if (profileSections is < 1 or > MaximumProfileSections)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profileSections),
                $"UYA collision hull profile sections must be between 1 and {MaximumProfileSections}.");
        }
        if (maximumFaces is < 1 or > DefaultMaximumFaces)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFaces),
                $"UYA collision hull maximum faces must be between 1 and {DefaultMaximumFaces}.");
        }
        if (triangles.Any(value => (uint)value.A >= positions.Count
            || (uint)value.B >= positions.Count || (uint)value.C >= positions.Count))
            throw new InvalidDataException($"{sourceLabel} contains an invalid triangle vertex index.");

        cancellationToken.ThrowIfCancellationRequested();
        var quantized = positions.Select(UyaInstancedCollisionSurfaceGenerator.Quantize).ToArray();
        var sourceVertexIndices = triangles
            .Where(triangle => !Degenerate(quantized[triangle.A], quantized[triangle.B], quantized[triangle.C]))
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .Order()
            .ToArray();
        var sourceTriangles = triangles
            .Where(triangle => !Degenerate(quantized[triangle.A], quantized[triangle.B], quantized[triangle.C]))
            .Select(triangle => (triangle.A, triangle.B, triangle.C))
            .ToArray();
        var vertices = sourceVertexIndices.Select(index => quantized[index]).Distinct().OrderBy(VertexKey).ToArray();
        if (vertices.Length < 4)
        {
            return FromPlanarSurface(
                positions, triangles, additionId, lodIndex, sourceLabel, rawType, maximumFaces, cancellationToken);
        }

        UyaCollisionSolidFace[]? faces = null;
        var generatedSections = 0;
        var maximumDeviation = float.PositiveInfinity;
        for (var sectionCount = 1; sectionCount <= profileSections; sectionCount++)
        {
            if (!TryBuildSectionedHull(
                    quantized, sourceTriangles, sectionCount, rawType, cancellationToken,
                    out var renderFaces, out var candidateSections)) continue;
            var candidateFaces = renderFaces.Select(ReverseWinding).ToArray();
            var candidateDeviation = UyaInstancedCollisionGeometry.MeasureMaximumSourceVertexDeviation(
                positions, sourceVertexIndices, candidateFaces, sourceLabel, cancellationToken);
            if (candidateDeviation >= maximumDeviation) continue;
            faces = candidateFaces;
            maximumDeviation = candidateDeviation;
            generatedSections = candidateSections;
        }
        if (faces is null)
            return FromPlanarSurface(
                positions, triangles, additionId, lodIndex, sourceLabel, rawType, maximumFaces, cancellationToken);
        faces = UyaInstancedCollisionGeometry.SplitForNativeOctants(
            faces, maximumFaces, cancellationToken);

        var analysis = UyaInstancedCollisionSurfaceGenerator.AnalyzeCandidate(additionId, faces, cancellationToken);
        return new(
            new(additionId, faces),
            GeneratorVersion,
            lodIndex,
            rawType,
            maximumFaces,
            generatedSections,
            sourceVertexIndices.Length,
            triangles.Count,
            faces.SelectMany(FaceVertices).Distinct().Count(),
            faces.Length,
            MergedQuadCount: 0,
            sourceVertexIndices.Length,
            maximumDeviation,
            analysis);
    }

    private static UyaInstancedCollisionHullCandidate FromPlanarSurface(
        IReadOnlyList<System.Numerics.Vector3> positions,
        IReadOnlyList<(int A, int B, int C)> triangles,
        string additionId,
        int lodIndex,
        string sourceLabel,
        byte rawType,
        int maximumFaces,
        CancellationToken cancellationToken)
    {
        var surface = UyaInstancedCollisionSurfaceGenerator.GenerateSurface(
            positions, triangles, additionId, lodIndex, sourceLabel, rawType, maximumFaces, cancellationToken);
        return new(
            surface.Addition,
            GeneratorVersion,
            lodIndex,
            rawType,
            maximumFaces,
            1,
            surface.SourceVertexCount,
            surface.SourceTriangleCount,
            surface.GeneratedVertexCount,
            surface.GeneratedFaceCount,
            surface.MergedQuadCount,
            surface.SourceVertexCount,
            surface.MaximumVertexDeviation,
            surface.Analysis);
    }

    private static bool TryBuildSectionedHull(
        IReadOnlyList<UyaCollisionVertex> sourceVertices,
        IReadOnlyList<(int A, int B, int C)> sourceTriangles,
        int requestedSections,
        byte rawType,
        CancellationToken cancellationToken,
        out UyaCollisionSolidFace[] faces,
        out int generatedSections)
    {
        faces = [];
        generatedSections = 0;
        var usedIndices = sourceTriangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .ToArray();
        var minimumZ = usedIndices.Min(index => sourceVertices[index].Z64);
        var maximumZ = usedIndices.Max(index => sourceVertices[index].Z64);
        var zSpan = (long)maximumZ - minimumZ;
        if (zSpan <= 0) return false;

        var sectionCount = (int)Math.Min(requestedSections, zSpan);
        for (; sectionCount >= 1; sectionCount--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = new List<UyaCollisionSolidFace>();
            var complete = true;
            for (var section = 0; section < sectionCount; section++)
            {
                var lower = checked((int)(minimumZ + zSpan * section / sectionCount));
                var upper = checked((int)(minimumZ + zSpan * (section + 1) / sectionCount));
                var clipped = CollectSectionVertices(
                    sourceVertices, sourceTriangles, lower, upper, cancellationToken);
                var vertices = BuildProfileRing(clipped.Where(vertex => vertex.Z64 == lower))
                    .Concat(BuildProfileRing(clipped.Where(vertex => vertex.Z64 == upper)))
                    .Distinct()
                    .OrderBy(VertexKey)
                    .ToArray();
                if (vertices.Length < 4 || !TryBuildHull(vertices, cancellationToken, out var hullFaces))
                {
                    complete = false;
                    break;
                }

                foreach (var face in hullFaces)
                {
                    var a = vertices[face.A];
                    var b = vertices[face.B];
                    var c = vertices[face.C];
                    var lowerCap = section > 0 && a.Z64 == lower && b.Z64 == lower && c.Z64 == lower;
                    var upperCap = section + 1 < sectionCount
                        && a.Z64 == upper && b.Z64 == upper && c.Z64 == upper;
                    if (lowerCap || upperCap) continue;
                    candidate.Add(new(rawType, a, b, c, default, IsQuad: false));
                }
            }

            if (!complete || candidate.Count == 0) continue;
            faces = candidate.Distinct().ToArray();
            generatedSections = sectionCount;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<UyaCollisionVertex> BuildProfileRing(
        IEnumerable<UyaCollisionVertex> candidates)
    {
        var points = candidates.Distinct().OrderBy(VertexKey).ToArray();
        if (points.Length <= 2) return points;

        var lower = new List<UyaCollisionVertex>();
        foreach (var point in points)
        {
            while (lower.Count >= 2 && Cross2D(lower[^2], lower[^1], point) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }
        var upper = new List<UyaCollisionVertex>();
        for (var index = points.Length - 1; index >= 0; index--)
        {
            var point = points[index];
            while (upper.Count >= 2 && Cross2D(upper[^2], upper[^1], point) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);

        // ponytail: Twelve vertices bound octant pressure; add radial detail only if measured silhouettes need it.
        while (lower.Count > MaximumRingVertices)
        {
            var remove = 0;
            var smallestArea = double.PositiveInfinity;
            for (var index = 0; index < lower.Count; index++)
            {
                var area = Math.Abs(Cross2D(
                    lower[(index + lower.Count - 1) % lower.Count],
                    lower[index],
                    lower[(index + 1) % lower.Count]));
                if (area >= smallestArea) continue;
                smallestArea = area;
                remove = index;
            }
            lower.RemoveAt(remove);
        }
        return lower;
    }

    private static double Cross2D(UyaCollisionVertex a, UyaCollisionVertex b, UyaCollisionVertex c) =>
        ((double)b.X64 - a.X64) * (c.Y64 - a.Y64)
        - ((double)b.Y64 - a.Y64) * (c.X64 - a.X64);

    private static UyaCollisionVertex[] CollectSectionVertices(
        IReadOnlyList<UyaCollisionVertex> vertices,
        IReadOnlyList<(int A, int B, int C)> triangles,
        int lower,
        int upper,
        CancellationToken cancellationToken)
    {
        var clipped = new List<UyaCollisionVertex>();
        foreach (var triangle in triangles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Point> polygon =
            [
                ToPoint(vertices[triangle.A]),
                ToPoint(vertices[triangle.B]),
                ToPoint(vertices[triangle.C]),
            ];
            polygon = ClipAtZ(polygon, lower, keepAbove: true);
            polygon = ClipAtZ(polygon, upper, keepAbove: false);
            clipped.AddRange(polygon.Select(Quantize));
        }
        return clipped.Distinct().OrderBy(VertexKey).ToArray();
    }

    private static IReadOnlyList<Point> ClipAtZ(IReadOnlyList<Point> polygon, int z, bool keepAbove)
    {
        if (polygon.Count == 0) return [];
        var output = new List<Point>();
        var previous = polygon[^1];
        var previousInside = keepAbove ? previous.Z >= z : previous.Z <= z;
        foreach (var current in polygon)
        {
            var currentInside = keepAbove ? current.Z >= z : current.Z <= z;
            if (currentInside != previousInside)
            {
                var amount = (z - previous.Z) / (current.Z - previous.Z);
                output.Add(new(
                    previous.X + (current.X - previous.X) * amount,
                    previous.Y + (current.Y - previous.Y) * amount,
                    z));
            }
            if (currentInside) output.Add(current);
            previous = current;
            previousInside = currentInside;
        }
        return output;
    }

    private static Point ToPoint(UyaCollisionVertex value) => new(value.X64, value.Y64, value.Z64);

    private static UyaCollisionVertex Quantize(Point value) => new(
        (int)Math.Round(value.X / 4, MidpointRounding.AwayFromZero) * 4,
        (int)Math.Round(value.Y / 4, MidpointRounding.AwayFromZero) * 4,
        (int)Math.Round(value.Z, MidpointRounding.AwayFromZero));

    private static bool TryBuildHull(
        IReadOnlyList<UyaCollisionVertex> vertices,
        CancellationToken cancellationToken,
        out List<Face> faces)
    {
        faces = [];
        var a = 0;
        var b = Farthest(vertices, index => DistanceSquared(vertices[a], vertices[index]));
        var c = Farthest(vertices, index => LineDistanceSquared(vertices[a], vertices[b], vertices[index]));
        var d = Farthest(vertices, index => Math.Abs(Signed(vertices[a], vertices[b], vertices[c], vertices[index])));
        if (DistanceSquared(vertices[a], vertices[b]) == 0
            || LineDistanceSquared(vertices[a], vertices[b], vertices[c]) == 0
            || Signed(vertices[a], vertices[b], vertices[c], vertices[d]) == 0)
        {
            return false;
        }

        var interior = new Point(
            ((double)vertices[a].X64 + vertices[b].X64 + vertices[c].X64 + vertices[d].X64) / 4,
            ((double)vertices[a].Y64 + vertices[b].Y64 + vertices[c].Y64 + vertices[d].Y64) / 4,
            ((double)vertices[a].Z64 + vertices[b].Z64 + vertices[c].Z64 + vertices[d].Z64) / 4);
        faces.Add(Oriented(a, b, c, vertices, interior));
        faces.Add(Oriented(a, d, b, vertices, interior));
        faces.Add(Oriented(b, d, c, vertices, interior));
        faces.Add(Oriented(c, d, a, vertices, interior));

        var seed = new HashSet<int> { a, b, c, d };
        for (var point = 0; point < vertices.Count; point++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seed.Contains(point)) continue;
            var visible = faces.Where(face => Signed(face, vertices, vertices[point]) > 0).ToArray();
            if (visible.Length == 0) continue;

            var edges = new Dictionary<Edge, (int A, int B, int Count)>();
            foreach (var face in visible)
            foreach (var (left, right) in face.Edges())
            {
                var key = Edge.Create(left, right);
                edges[key] = edges.TryGetValue(key, out var value)
                    ? (value.A, value.B, value.Count + 1)
                    : (left, right, 1);
            }

            faces.RemoveAll(visible.Contains);
            foreach (var edge in edges.Values.Where(value => value.Count == 1))
                faces.Add(Oriented(edge.A, edge.B, point, vertices, interior));
        }

        return faces.Count >= 4;
    }

    private static Face Oriented(
        int a,
        int b,
        int c,
        IReadOnlyList<UyaCollisionVertex> vertices,
        Point interior) => Signed(vertices[a], vertices[b], vertices[c], interior) <= 0
            ? new(a, b, c)
            : new(a, c, b);

    private static int Farthest(IReadOnlyList<UyaCollisionVertex> vertices, Func<int, double> measure)
    {
        var best = 0;
        var distance = double.NegativeInfinity;
        for (var index = 0; index < vertices.Count; index++)
        {
            var next = measure(index);
            if (next > distance)
            {
                best = index;
                distance = next;
            }
        }
        return best;
    }

    private static double Signed(Face face, IReadOnlyList<UyaCollisionVertex> vertices, UyaCollisionVertex point) =>
        Signed(vertices[face.A], vertices[face.B], vertices[face.C], point);

    private static double Signed(
        UyaCollisionVertex a,
        UyaCollisionVertex b,
        UyaCollisionVertex c,
        UyaCollisionVertex point) => Signed(a, b, c, new Point(point.X64, point.Y64, point.Z64));

    private static double Signed(
        UyaCollisionVertex a,
        UyaCollisionVertex b,
        UyaCollisionVertex c,
        Point point)
    {
        var ab = new Point(b.X64 - a.X64, b.Y64 - a.Y64, b.Z64 - a.Z64);
        var ac = new Point(c.X64 - a.X64, c.Y64 - a.Y64, c.Z64 - a.Z64);
        var ap = new Point(point.X - a.X64, point.Y - a.Y64, point.Z - a.Z64);
        return (ab.Y * ac.Z - ab.Z * ac.Y) * ap.X
            + (ab.Z * ac.X - ab.X * ac.Z) * ap.Y
            + (ab.X * ac.Y - ab.Y * ac.X) * ap.Z;
    }

    private static double DistanceSquared(UyaCollisionVertex a, UyaCollisionVertex b) =>
        Square((double)b.X64 - a.X64) + Square((double)b.Y64 - a.Y64) + Square((double)b.Z64 - a.Z64);

    private static double LineDistanceSquared(
        UyaCollisionVertex a,
        UyaCollisionVertex b,
        UyaCollisionVertex point)
    {
        var abX = (double)b.X64 - a.X64;
        var abY = (double)b.Y64 - a.Y64;
        var abZ = (double)b.Z64 - a.Z64;
        var apX = (double)point.X64 - a.X64;
        var apY = (double)point.Y64 - a.Y64;
        var apZ = (double)point.Z64 - a.Z64;
        return Square(abY * apZ - abZ * apY)
            + Square(abZ * apX - abX * apZ)
            + Square(abX * apY - abY * apX);
    }

    private static bool Degenerate(UyaCollisionVertex a, UyaCollisionVertex b, UyaCollisionVertex c) =>
        Signed(a, b, c, new Point(a.X64 + 1, a.Y64, a.Z64)) == 0
        && Signed(a, b, c, new Point(a.X64, a.Y64 + 1, a.Z64)) == 0
        && Signed(a, b, c, new Point(a.X64, a.Y64, a.Z64 + 1)) == 0;

    private static UyaCollisionSolidFace ReverseWinding(UyaCollisionSolidFace face) => face.IsQuad
        ? new(face.Type, face.A, face.D, face.C, face.B, IsQuad: true)
        : new(face.Type, face.A, face.C, face.B, default, IsQuad: false);

    private static IEnumerable<UyaCollisionVertex> FaceVertices(UyaCollisionSolidFace face) => face.IsQuad
        ? [face.A, face.B, face.C, face.D]
        : [face.A, face.B, face.C];

    private static (int X, int Y, int Z) VertexKey(UyaCollisionVertex value) =>
        (value.X64, value.Y64, value.Z64);

    private static double Square(double value) => value * value;

    private readonly record struct Point(double X, double Y, double Z);

    private readonly record struct Face(int A, int B, int C)
    {
        public IEnumerable<(int A, int B)> Edges()
        {
            yield return (A, B);
            yield return (B, C);
            yield return (C, A);
        }
    }

    private readonly record struct Edge(int A, int B)
    {
        public static Edge Create(int a, int b) => a < b ? new(a, b) : new(b, a);
    }
}
