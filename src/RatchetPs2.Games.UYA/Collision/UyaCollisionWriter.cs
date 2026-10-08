using System.Numerics;

namespace RatchetPs2.Games.UYA.Collision;

public static class UyaCollisionWriter
{
    private const int OctantSize64 = 4 * 64;
    private const int OctantHalfSize64 = OctantSize64 / 2;
    private const int MaximumOctants = 1_000_000;

    public static byte[] Write(
        UyaMapCollision collision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collision);
        var octants = BuildOctants(collision.SolidPieces, new Dictionary<int, string>(), cancellationToken);
        var mesh = WriteSolidMesh(octants, cancellationToken);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write(0);
        writer.Write(0);
        Pad(output, 0x40);
        var meshOffset = checked((int)output.Position);
        writer.Write(mesh);
        var barrierOffset = 0;
        if (collision.PlayerBarriers.Count > 0)
        {
            Pad(output, 0x40);
            barrierOffset = checked((int)output.Position);
            writer.Write(WritePlayerBarriers(collision.PlayerBarriers, cancellationToken));
        }

        PatchInt32(writer, 0, meshOffset);
        PatchInt32(writer, 4, barrierOffset);
        return output.ToArray();
    }

    public static UyaCollisionAnalysis Analyze(
        UyaMapCollision collision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collision);
        return Analyze(collision, new Dictionary<int, string>(), cancellationToken);
    }

    internal static UyaCollisionAnalysis Analyze(
        UyaMapCollision collision,
        IReadOnlyDictionary<int, string> additionIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(additionIds);
        var faces = collision.SolidPieces.SelectMany(piece => piece.Faces).ToArray();
        var octants = BuildOctants(collision.SolidPieces, additionIds, cancellationToken);
        var vertices = new HashSet<UyaCollisionVertex>();
        foreach (var face in faces)
        {
            vertices.Add(face.A);
            vertices.Add(face.B);
            vertices.Add(face.C);
            if (face.IsQuad) vertices.Add(face.D);
        }

        return new(
            faces.Length,
            vertices.Count,
            octants.Count,
            checked(octants.Values.Sum(octant => octant.Faces.Length) - faces.Length),
            octants.Values.Sum(octant => octant.Violations.Count),
            octants.Values
                .OrderBy(octant => octant.Key.Z)
                .ThenBy(octant => octant.Key.Y)
                .ThenBy(octant => octant.Key.X)
                .Select(octant => new UyaCollisionOctantCost(
                    octant.Key.X,
                    octant.Key.Y,
                    octant.Key.Z,
                    octant.Faces.Length,
                    octant.Vertices.Length,
                    octant.QuadCount,
                    octant.EncodedByteCount,
                    octant.AdditionIds,
                    octant.Violations))
                .ToArray());
    }

    private static Dictionary<OctantKey, OctantData> BuildOctants(
        IReadOnlyList<UyaCollisionSolidPiece> pieces,
        IReadOnlyDictionary<int, string> additionIds,
        CancellationToken cancellationToken)
    {
        var octants = new Dictionary<OctantKey, List<TaggedFace>>();
        foreach (var piece in pieces)
        {
            additionIds.TryGetValue(piece.SourceIndex, out var additionId);
            foreach (var face in piece.Faces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var vertices = face.IsQuad
                    ? new[] { face.A, face.B, face.C, face.D }
                    : new[] { face.A, face.B, face.C };
                var minX = vertices.Min(vertex => vertex.X64);
                var minY = vertices.Min(vertex => vertex.Y64);
                var minZ = vertices.Min(vertex => vertex.Z64);
                var maxX = vertices.Max(vertex => vertex.X64);
                var maxY = vertices.Max(vertex => vertex.Y64);
                var maxZ = vertices.Max(vertex => vertex.Z64);
                var x0 = FloorDiv(minX, OctantSize64);
                var y0 = FloorDiv(minY, OctantSize64);
                var z0 = FloorDiv(minZ, OctantSize64);
                var x1 = MaximumOctant(minX, maxX);
                var y1 = MaximumOctant(minY, maxY);
                var z1 = MaximumOctant(minZ, maxZ);
                var candidateCount = checked(
                    ((long)x1 - x0 + 1)
                    * ((long)y1 - y0 + 1)
                    * ((long)z1 - z0 + 1));
                if (candidateCount > MaximumOctants)
                {
                    throw new InvalidDataException(
                        $"UYA collision face spans {candidateCount} octants; maximum is {MaximumOctants}.");
                }

                var assigned = false;
                for (var z = z0; z <= z1; z++)
                {
                    for (var y = y0; y <= y1; y++)
                    {
                        for (var x = x0; x <= x1; x++)
                        {
                            if (!Intersects(face, x, y, z)
                                || additionId is null && !CanPack(face, x, y, z))
                            {
                                continue;
                            }

                            var key = new OctantKey(x, y, z);
                            if (!octants.TryGetValue(key, out var octantFaces))
                            {
                                if (octants.Count >= MaximumOctants)
                                {
                                    throw new InvalidDataException(
                                        $"UYA collision exceeds the {MaximumOctants}-octant safety limit.");
                                }

                                octantFaces = [];
                                octants.Add(key, octantFaces);
                            }

                            octantFaces.Add(new(face, additionId));
                            assigned = true;
                        }
                    }
                }
                if (!assigned && additionId is null)
                {
                    throw new InvalidDataException(
                        "UYA collision face cannot fit in a native octant.");
                }
            }
        }

        return octants.ToDictionary(
            item => item.Key,
            item => BuildOctant(item.Key, item.Value));
    }

    private static byte[] WriteSolidMesh(
        Dictionary<OctantKey, OctantData> octants,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        if (octants.Count == 0)
        {
            writer.Write((short)0);
            writer.Write((ushort)0);
            return stream.ToArray();
        }

        var violation = octants.Values.SelectMany(octant => octant.Violations).FirstOrDefault();
        if (violation is not null) throw new InvalidDataException(violation);

        ValidateCoordinateRange(octants.Keys.Select(key => key.Z), "Z");
        var minimumZ = octants.Keys.Min(key => key.Z);
        var maximumZ = octants.Keys.Max(key => key.Z);
        var zCount = SpanCount(minimumZ, maximumZ, "Z");
        writer.Write(checked((short)minimumZ));
        writer.Write(zCount);
        var zPatches = Reserve(stream, checked(zCount * sizeof(ushort)));
        var yPatches = new Dictionary<(int Z, int Y), long>();
        var xPatches = new Dictionary<OctantKey, long>();

        foreach (var zGroup in octants.GroupBy(item => item.Key.Z).OrderBy(group => group.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Pad(stream, 4);
            var offset = checked((int)stream.Position);
            if (offset / 4 > ushort.MaxValue)
            {
                throw new InvalidDataException("UYA collision Z lookup offset exceeds its native 16-bit range.");
            }

            PatchUInt16(writer, zPatches + (zGroup.Key - minimumZ) * sizeof(ushort), checked((ushort)(offset / 4)));
            ValidateCoordinateRange(zGroup.Select(item => item.Key.Y), "Y");
            var minimumY = zGroup.Min(item => item.Key.Y);
            var maximumY = zGroup.Max(item => item.Key.Y);
            var yCount = SpanCount(minimumY, maximumY, "Y");
            writer.Write(checked((short)minimumY));
            writer.Write(yCount);
            var patches = Reserve(stream, checked(yCount * sizeof(uint)));
            foreach (var y in zGroup.Select(item => item.Key.Y).Distinct())
            {
                yPatches.Add((zGroup.Key, y), patches + (y - minimumY) * sizeof(uint));
            }
        }

        foreach (var yGroup in octants
            .GroupBy(item => (item.Key.Z, item.Key.Y))
            .OrderBy(group => group.Key.Z)
            .ThenBy(group => group.Key.Y))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Pad(stream, 4);
            var offset = checked((int)stream.Position);
            PatchUInt32(writer, yPatches[yGroup.Key], checked((uint)offset));
            ValidateCoordinateRange(yGroup.Select(item => item.Key.X), "X");
            var minimumX = yGroup.Min(item => item.Key.X);
            var maximumX = yGroup.Max(item => item.Key.X);
            var xCount = SpanCount(minimumX, maximumX, "X");
            writer.Write(checked((short)minimumX));
            writer.Write(xCount);
            var patches = Reserve(stream, checked(xCount * sizeof(uint)));
            foreach (var item in yGroup)
            {
                xPatches.Add(item.Key, patches + (item.Key.X - minimumX) * sizeof(uint));
            }
        }

        foreach (var (key, octant) in octants
            .OrderBy(item => item.Key.Z)
            .ThenBy(item => item.Key.Y)
            .ThenBy(item => item.Key.X))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Pad(stream, 0x10);
            var offset = checked((int)stream.Position);
            if (offset > 0x00ff_ffff)
            {
                throw new InvalidDataException("UYA collision octant offset exceeds its native 24-bit range.");
            }

            var bytes = WriteOctant(octant);
            writer.Write(bytes);
            var sizeUnits = octant.EncodedByteCount / 0x10;
            PatchUInt32(writer, xPatches[key], ((uint)offset << 8) | (uint)sizeUnits);
        }

        return stream.ToArray();
    }

    private static OctantData BuildOctant(OctantKey key, IReadOnlyList<TaggedFace> sourceFaces)
    {
        var faces = sourceFaces
            .OrderByDescending(item => item.Face.IsQuad)
            .ThenBy(item => item.Face.Type)
            .ThenBy(item => item.Face.A.X64)
            .ThenBy(item => item.Face.A.Y64)
            .ThenBy(item => item.Face.A.Z64)
            .ThenBy(item => item.Face.B.X64)
            .ThenBy(item => item.Face.B.Y64)
            .ThenBy(item => item.Face.B.Z64)
            .ThenBy(item => item.Face.C.X64)
            .ThenBy(item => item.Face.C.Y64)
            .ThenBy(item => item.Face.C.Z64)
            .ThenBy(item => item.Face.D.X64)
            .ThenBy(item => item.Face.D.Y64)
            .ThenBy(item => item.Face.D.Z64)
            .Select(item => item.Face)
            .ToArray();
        var vertices = new List<UyaCollisionVertex>();
        var vertexIndexes = new Dictionary<UyaCollisionVertex, int>();
        foreach (var face in faces)
        {
            AddVertex(face.A);
            AddVertex(face.B);
            AddVertex(face.C);
            if (face.IsQuad) AddVertex(face.D);
        }

        var quadCount = faces.Count(face => face.IsQuad);
        var encodedByteCount = checked(4 + vertices.Count * 4 + faces.Length * 4 + quadCount);
        encodedByteCount = checked((encodedByteCount + 0x0f) & ~0x0f);
        var violations = new List<string>();
        if (faces.Length > ushort.MaxValue)
            violations.Add("UYA collision octant exceeds its native face count.");
        if (vertices.Count > byte.MaxValue)
            violations.Add("UYA collision octant exceeds its native vertex count.");
        if (quadCount > byte.MaxValue)
            violations.Add("UYA collision octant exceeds its native quad count.");
        if (encodedByteCount / 0x10 > byte.MaxValue)
            violations.Add("UYA collision octant size exceeds its native 8-bit range.");
        if (faces.Any(face => !CanPack(face, key.X, key.Y, key.Z)))
            violations.Add("UYA collision octant contains a face outside its native packed coordinate range.");

        return new(
            key,
            faces,
            vertices.ToArray(),
            vertexIndexes,
            quadCount,
            encodedByteCount,
            sourceFaces
                .Select(item => item.AdditionId)
                .Where(id => id is not null)
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            violations);

        void AddVertex(UyaCollisionVertex vertex)
        {
            if (vertexIndexes.ContainsKey(vertex)) return;
            vertexIndexes.Add(vertex, vertices.Count);
            vertices.Add(vertex);
        }
    }

    private static byte[] WriteOctant(OctantData octant)
    {
        var faces = octant.Faces;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)faces.Length));
        writer.Write(checked((byte)octant.Vertices.Length));
        writer.Write(checked((byte)octant.QuadCount));
        var centerX64 = checked(octant.Key.X * OctantSize64 + OctantHalfSize64);
        var centerY64 = checked(octant.Key.Y * OctantSize64 + OctantHalfSize64);
        var centerZ64 = checked(octant.Key.Z * OctantSize64 + OctantHalfSize64);
        foreach (var vertex in octant.Vertices)
        {
            writer.Write(PackVertex(
                checked(vertex.X64 - centerX64),
                checked(vertex.Y64 - centerY64),
                checked(vertex.Z64 - centerZ64)));
        }

        foreach (var face in faces)
        {
            writer.Write(checked((byte)octant.VertexIndexes[face.A]));
            writer.Write(checked((byte)octant.VertexIndexes[face.B]));
            writer.Write(checked((byte)octant.VertexIndexes[face.C]));
            writer.Write(face.Type);
        }

        foreach (var face in faces.Where(face => face.IsQuad))
        {
            writer.Write(checked((byte)octant.VertexIndexes[face.D]));
        }

        Pad(stream, 0x10);
        var bytes = stream.ToArray();
        if (bytes.Length != octant.EncodedByteCount)
        {
            throw new InvalidDataException("UYA collision octant analysis did not match encoded size.");
        }

        return bytes;
    }

    private static byte[] WritePlayerBarriers(
        IReadOnlyList<UyaCollisionPlayerBarrier> barriers,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(barriers.Count);
        Pad(stream, 0x10);
        var table = Reserve(stream, checked(barriers.Count * 0x10));
        for (var index = 0; index < barriers.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var barrier = barriers[index];
            if (barrier.Vertices.Count > ushort.MaxValue || barrier.Triangles.Count > ushort.MaxValue)
            {
                throw new InvalidDataException($"UYA player barrier {index} exceeds its native count range.");
            }

            ValidateUnsigned16(barrier.BoundingSphere.Center, $"player barrier {index} sphere");
            if (barrier.BoundingSphere.Radius64 is < 0 or > ushort.MaxValue)
            {
                throw new InvalidDataException($"UYA player barrier {index} radius is outside its native range.");
            }

            Pad(stream, 0x10);
            var dataOffset = checked((int)stream.Position);
            var entry = table + index * 0x10;
            PatchUInt16(writer, entry, checked((ushort)barrier.BoundingSphere.Center.X64));
            PatchUInt16(writer, entry + 2, checked((ushort)barrier.BoundingSphere.Center.Y64));
            PatchUInt16(writer, entry + 4, checked((ushort)barrier.BoundingSphere.Center.Z64));
            PatchUInt16(writer, entry + 6, checked((ushort)barrier.BoundingSphere.Radius64));
            PatchUInt16(writer, entry + 8, checked((ushort)barrier.Triangles.Count));
            PatchUInt16(writer, entry + 10, checked((ushort)barrier.Vertices.Count));
            PatchUInt32(writer, entry + 12, checked((uint)dataOffset));
            foreach (var vertex in barrier.Vertices)
            {
                ValidateUnsigned16(vertex, $"player barrier {index} vertex");
                writer.Write(checked((ushort)vertex.X64));
                writer.Write(checked((ushort)vertex.Y64));
                writer.Write(checked((ushort)vertex.Z64));
                writer.Write((ushort)0);
            }

            foreach (var triangle in barrier.Triangles)
            {
                if (triangle.A >= barrier.Vertices.Count
                    || triangle.B >= barrier.Vertices.Count
                    || triangle.C >= barrier.Vertices.Count)
                {
                    throw new InvalidDataException(
                        $"UYA player barrier {index} triangle references an out-of-range vertex.");
                }

                writer.Write(triangle.A);
                writer.Write(triangle.B);
                writer.Write(triangle.C);
                writer.Write((byte)0);
            }
        }

        return stream.ToArray();
    }

    private static uint PackVertex(int x64, int y64, int z64)
    {
        if ((x64 & 3) != 0 || (y64 & 3) != 0)
        {
            throw new InvalidDataException("UYA solid collision X/Y coordinates require 1/16-unit precision.");
        }

        var x16 = x64 / 4;
        var y16 = y64 / 4;
        if (x16 is < -512 or > 511 || y16 is < -512 or > 511 || z64 is < -2048 or > 2047)
        {
            throw new InvalidDataException("UYA solid collision vertex is outside its octant-local packed range.");
        }

        return (uint)((x16 & 0x3ff) | ((y16 & 0x3ff) << 10) | ((z64 & 0xfff) << 20));
    }

    private static bool Intersects(UyaCollisionSolidFace face, int x, int y, int z)
    {
        var center = new Vector3(
            checked(x * OctantSize64 + OctantHalfSize64),
            checked(y * OctantSize64 + OctantHalfSize64),
            checked(z * OctantSize64 + OctantHalfSize64));
        return TriangleIntersectsBox(face.A, face.B, face.C, center, OctantHalfSize64)
            || face.IsQuad && TriangleIntersectsBox(face.A, face.C, face.D, center, OctantHalfSize64);
    }

    private static bool CanPack(UyaCollisionSolidFace face, int x, int y, int z)
    {
        var centerX64 = checked(x * OctantSize64 + OctantHalfSize64);
        var centerY64 = checked(y * OctantSize64 + OctantHalfSize64);
        var centerZ64 = checked(z * OctantSize64 + OctantHalfSize64);
        return Vertices(face).All(vertex =>
        {
            var x64 = checked(vertex.X64 - centerX64);
            var y64 = checked(vertex.Y64 - centerY64);
            var z64 = checked(vertex.Z64 - centerZ64);
            return (x64 & 3) == 0
                && (y64 & 3) == 0
                && x64 / 4 is >= -512 and <= 511
                && y64 / 4 is >= -512 and <= 511
                && z64 is >= -2048 and <= 2047;
        });
    }

    internal static bool CanFitNativeOctant(UyaCollisionSolidFace face)
    {
        var vertices = Vertices(face).ToArray();
        var x0 = FloorDiv(vertices.Min(vertex => vertex.X64), OctantSize64);
        var y0 = FloorDiv(vertices.Min(vertex => vertex.Y64), OctantSize64);
        var z0 = FloorDiv(vertices.Min(vertex => vertex.Z64), OctantSize64);
        var x1 = MaximumOctant(vertices.Min(vertex => vertex.X64), vertices.Max(vertex => vertex.X64));
        var y1 = MaximumOctant(vertices.Min(vertex => vertex.Y64), vertices.Max(vertex => vertex.Y64));
        var z1 = MaximumOctant(vertices.Min(vertex => vertex.Z64), vertices.Max(vertex => vertex.Z64));
        var candidateCount = checked(
            ((long)x1 - x0 + 1) * ((long)y1 - y0 + 1) * ((long)z1 - z0 + 1));
        if (candidateCount > MaximumOctants) return false;
        for (var z = z0; z <= z1; z++)
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
            if (Intersects(face, x, y, z) && CanPack(face, x, y, z)) return true;
        return false;
    }

    private static IEnumerable<UyaCollisionVertex> Vertices(UyaCollisionSolidFace face)
    {
        yield return face.A;
        yield return face.B;
        yield return face.C;
        if (face.IsQuad) yield return face.D;
    }

    internal static bool TriangleIntersectsBox(
        UyaCollisionVertex a,
        UyaCollisionVertex b,
        UyaCollisionVertex c,
        Vector3 center,
        float halfSize)
    {
        var vertices = new[] { ToVector(a) - center, ToVector(b) - center, ToVector(c) - center };
        var edges = new[] { vertices[1] - vertices[0], vertices[2] - vertices[1], vertices[0] - vertices[2] };
        foreach (var edge in edges)
        {
            if (!Overlaps(Vector3.Cross(edge, new Vector3(1, 0, 0)), vertices, halfSize)
                || !Overlaps(Vector3.Cross(edge, new Vector3(0, 1, 0)), vertices, halfSize)
                || !Overlaps(Vector3.Cross(edge, new Vector3(0, 0, 1)), vertices, halfSize))
            {
                return false;
            }
        }

        return Overlaps(new Vector3(1, 0, 0), vertices, halfSize)
            && Overlaps(new Vector3(0, 1, 0), vertices, halfSize)
            && Overlaps(new Vector3(0, 0, 1), vertices, halfSize)
            && Overlaps(Vector3.Cross(edges[0], edges[1]), vertices, halfSize);
    }

    private static bool Overlaps(Vector3 axis, IReadOnlyList<Vector3> vertices, float halfSize)
    {
        if (axis.LengthSquared() < 0.0001f) return true;
        var first = Vector3.Dot(vertices[0], axis);
        var minimum = first;
        var maximum = first;
        for (var index = 1; index < vertices.Count; index++)
        {
            var value = Vector3.Dot(vertices[index], axis);
            minimum = MathF.Min(minimum, value);
            maximum = MathF.Max(maximum, value);
        }

        var radius = halfSize * (MathF.Abs(axis.X) + MathF.Abs(axis.Y) + MathF.Abs(axis.Z));
        return minimum <= radius && maximum >= -radius;
    }

    private static Vector3 ToVector(UyaCollisionVertex vertex) => new(vertex.X64, vertex.Y64, vertex.Z64);

    private static int MaximumOctant(int minimum, int maximum)
    {
        var result = FloorDiv(maximum, OctantSize64);
        return maximum != minimum && maximum % OctantSize64 == 0
            ? checked(result - 1)
            : result;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var result = value / divisor;
        return value % divisor < 0 ? result - 1 : result;
    }

    private static ushort SpanCount(int minimum, int maximum, string axis)
    {
        var count = checked((long)maximum - minimum + 1);
        if (count > ushort.MaxValue)
        {
            throw new InvalidDataException($"UYA collision {axis} lookup span exceeds its native 16-bit count.");
        }

        return checked((ushort)count);
    }

    private static void ValidateCoordinateRange(IEnumerable<int> values, string axis)
    {
        if (values.Any(value => value is < short.MinValue or > short.MaxValue))
        {
            throw new InvalidDataException($"UYA collision {axis} octant coordinate exceeds its native signed 16-bit range.");
        }
    }

    private static void ValidateUnsigned16(UyaCollisionVertex vertex, string name)
    {
        if (vertex.X64 is < 0 or > ushort.MaxValue
            || vertex.Y64 is < 0 or > ushort.MaxValue
            || vertex.Z64 is < 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException($"UYA {name} is outside its native unsigned 16-bit range.");
        }
    }

    private static long Reserve(Stream stream, int count)
    {
        var offset = stream.Position;
        stream.Write(new byte[count]);
        return offset;
    }

    private static void Pad(Stream stream, int alignment)
    {
        var padding = (alignment - stream.Position % alignment) % alignment;
        if (padding > 0) stream.Write(new byte[padding]);
    }

    private static void PatchInt32(BinaryWriter writer, long offset, int value) => Patch(writer, offset, () => writer.Write(value));

    private static void PatchUInt16(BinaryWriter writer, long offset, ushort value) => Patch(writer, offset, () => writer.Write(value));

    private static void PatchUInt32(BinaryWriter writer, long offset, uint value) => Patch(writer, offset, () => writer.Write(value));

    private static void Patch(BinaryWriter writer, long offset, Action write)
    {
        var position = writer.BaseStream.Position;
        writer.BaseStream.Position = offset;
        write();
        writer.BaseStream.Position = position;
    }

    private readonly record struct OctantKey(int X, int Y, int Z);

    private readonly record struct TaggedFace(UyaCollisionSolidFace Face, string? AdditionId);

    private sealed record OctantData(
        OctantKey Key,
        UyaCollisionSolidFace[] Faces,
        UyaCollisionVertex[] Vertices,
        IReadOnlyDictionary<UyaCollisionVertex, int> VertexIndexes,
        int QuadCount,
        int EncodedByteCount,
        IReadOnlyList<string> AdditionIds,
        IReadOnlyList<string> Violations);

}
