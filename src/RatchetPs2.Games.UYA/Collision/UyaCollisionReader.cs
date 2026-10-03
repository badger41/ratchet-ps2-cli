using RatchetPs2.Core.IO;
using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Collision;

public static class UyaCollisionReader
{
    private const int HeaderSize = 8;

    public static UyaMapCollision Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Read(data.AsSpan());
    }

    public static UyaMapCollision ReadChunkWad(byte[] chunkWad)
    {
        ArgumentNullException.ThrowIfNull(chunkWad);
        var collision = TfragChunkWadReader.ReadCollisionPayload(chunkWad);
        if (collision.Length == 0)
        {
            throw new InvalidDataException("UYA chunk WAD has no collision payload.");
        }

        return Read(collision);
    }

    public static UyaMapCollision Read(ReadOnlySpan<byte> data)
    {
        RequireRange(data, 0, HeaderSize, "header");
        var meshOffset = ReadInt32(data, 0, "solid mesh offset");
        var barrierOffset = ReadInt32(data, 4, "player barrier offset");
        if (meshOffset < HeaderSize || meshOffset >= data.Length || (meshOffset & 3) != 0)
        {
            throw Invalid(0, $"solid mesh offset 0x{meshOffset:X} is invalid");
        }

        if (barrierOffset != 0
            && (barrierOffset <= meshOffset || barrierOffset >= data.Length || (barrierOffset & 3) != 0))
        {
            throw Invalid(4, $"player barrier offset 0x{barrierOffset:X} is invalid");
        }

        var meshEnd = barrierOffset == 0 ? data.Length : barrierOffset;
        var solid = ReadSolidMesh(data[meshOffset..meshEnd], meshOffset);
        var barriers = barrierOffset == 0
            ? []
            : ReadPlayerBarriers(data[barrierOffset..], barrierOffset);
        var pieces = BuildSolidPieces(solid.Faces);
        return new(
            pieces,
            barriers,
            solid.OctantCount,
            solid.NativeFaceCount,
            solid.NativeFaceCount - solid.Faces.Count);
    }

    private static SolidReadResult ReadSolidMesh(ReadOnlySpan<byte> mesh, int sourceOffset)
    {
        RequireRange(mesh, 0, 4, "solid mesh root", sourceOffset);
        var ranges = new RangeTracker(mesh.Length, sourceOffset, "solid mesh");
        var zCoordinate = ReadInt16(mesh, 0, "solid Z coordinate", sourceOffset);
        var zCount = ReadUInt16(mesh, 2, "solid Z count", sourceOffset);
        ranges.Add(0, checked(4 + zCount * 2), "Z lookup");
        var faces = new HashSet<UyaCollisionSolidFace>();
        var nativeFaceCount = 0;
        var octantCount = 0;

        for (var zIndex = 0; zIndex < zCount; zIndex++)
        {
            var zEntryOffset = 4 + zIndex * 2;
            var zOffset = checked(ReadUInt16(mesh, zEntryOffset, "Z lookup offset", sourceOffset) * 4);
            if (zOffset == 0)
            {
                continue;
            }

            RequireAligned(zOffset, 4, sourceOffset, "Y lookup");
            var yCoordinate = ReadInt16(mesh, zOffset, "solid Y coordinate", sourceOffset);
            var yCount = ReadUInt16(mesh, zOffset + 2, "solid Y count", sourceOffset);
            ranges.Add(zOffset, checked(4 + yCount * 4), $"Y lookup {zIndex}");

            for (var yIndex = 0; yIndex < yCount; yIndex++)
            {
                var yEntryOffset = checked(zOffset + 4 + yIndex * 4);
                var yOffsetValue = ReadUInt32(mesh, yEntryOffset, "Y lookup offset", sourceOffset);
                if (yOffsetValue == 0)
                {
                    continue;
                }

                if (yOffsetValue > int.MaxValue)
                {
                    throw Invalid(sourceOffset + yEntryOffset, "X lookup offset exceeds the supported range");
                }

                var xOffset = (int)yOffsetValue;
                RequireAligned(xOffset, 4, sourceOffset, "X lookup");
                var xCoordinate = ReadInt16(mesh, xOffset, "solid X coordinate", sourceOffset);
                var xCount = ReadUInt16(mesh, xOffset + 2, "solid X count", sourceOffset);
                ranges.Add(xOffset, checked(4 + xCount * 4), $"X lookup {zIndex}/{yIndex}");

                for (var xIndex = 0; xIndex < xCount; xIndex++)
                {
                    var xEntryOffset = checked(xOffset + 4 + xIndex * 4);
                    var packed = ReadUInt32(mesh, xEntryOffset, "octant lookup entry", sourceOffset);
                    if (packed == 0)
                    {
                        continue;
                    }

                    var octantOffsetValue = packed >> 8;
                    var octantSize = checked((int)(packed & 0xff) * 0x10);
                    if (octantOffsetValue > int.MaxValue || octantSize == 0)
                    {
                        throw Invalid(sourceOffset + xEntryOffset, "octant lookup entry has an invalid offset or size");
                    }

                    var octantOffset = (int)octantOffsetValue;
                    RequireAligned(octantOffset, 0x10, sourceOffset, "octant");
                    ranges.Add(octantOffset, octantSize, $"octant {zIndex}/{yIndex}/{xIndex}");
                    var octantFaces = ReadOctant(
                        mesh,
                        octantOffset,
                        octantSize,
                        xCoordinate + xIndex,
                        yCoordinate + yIndex,
                        zCoordinate + zIndex,
                        sourceOffset);
                    octantCount++;
                    nativeFaceCount = checked(nativeFaceCount + octantFaces.Count);
                    foreach (var face in octantFaces)
                    {
                        faces.Add(face);
                    }
                }
            }
        }

        return new(faces.Order(FaceComparer.Instance).ToArray(), octantCount, nativeFaceCount);
    }

    private static IReadOnlyList<UyaCollisionSolidFace> ReadOctant(
        ReadOnlySpan<byte> mesh,
        int offset,
        int declaredSize,
        int x,
        int y,
        int z,
        int sourceOffset)
    {
        RequireRange(mesh, offset, 4, "octant header", sourceOffset);
        var faceCount = ReadUInt16(mesh, offset, "octant face count", sourceOffset);
        var vertexCount = mesh[offset + 2];
        var quadCount = mesh[offset + 3];
        if (quadCount > faceCount)
        {
            throw Invalid(sourceOffset + offset + 3, "octant quad count exceeds its face count");
        }

        var requiredSize = checked(4 + vertexCount * 4 + faceCount * 4 + quadCount);
        if (requiredSize > declaredSize)
        {
            throw Invalid(sourceOffset + offset, $"octant requires 0x{requiredSize:X} bytes but declares 0x{declaredSize:X}");
        }

        RequireRange(mesh, offset, requiredSize, "octant payload", sourceOffset);
        var centerX64 = checked((x * 4 + 2) * 64);
        var centerY64 = checked((y * 4 + 2) * 64);
        var centerZ64 = checked((z * 4 + 2) * 64);
        var vertices = new UyaCollisionVertex[vertexCount];
        var cursor = offset + 4;
        for (var index = 0; index < vertices.Length; index++)
        {
            var packed = ReadUInt32(mesh, cursor, "octant vertex", sourceOffset);
            vertices[index] = new(
                checked(centerX64 + SignExtend((int)(packed & 0x3ff), 10) * 4),
                checked(centerY64 + SignExtend((int)((packed >> 10) & 0x3ff), 10) * 4),
                checked(centerZ64 + SignExtend((int)((packed >> 20) & 0xfff), 12)));
            cursor += 4;
        }

        var faceOffset = cursor;
        var fourthVertexOffset = checked(faceOffset + faceCount * 4);
        var faces = new UyaCollisionSolidFace[faceCount];
        for (var index = 0; index < faces.Length; index++)
        {
            var current = faceOffset + index * 4;
            var a = mesh[current];
            var b = mesh[current + 1];
            var c = mesh[current + 2];
            var type = mesh[current + 3];
            var d = index < quadCount ? mesh[fourthVertexOffset + index] : (byte)0;
            ValidateVertexIndex(a, vertexCount, sourceOffset + current);
            ValidateVertexIndex(b, vertexCount, sourceOffset + current + 1);
            ValidateVertexIndex(c, vertexCount, sourceOffset + current + 2);
            if (index < quadCount)
            {
                ValidateVertexIndex(d, vertexCount, sourceOffset + fourthVertexOffset + index);
            }

            faces[index] = Canonicalize(new(
                type,
                vertices[a],
                vertices[b],
                vertices[c],
                index < quadCount ? vertices[d] : default,
                index < quadCount));
        }

        return faces;
    }

    private static IReadOnlyList<UyaCollisionPlayerBarrier> ReadPlayerBarriers(
        ReadOnlySpan<byte> data,
        int sourceOffset)
    {
        RequireRange(data, 0, 0x10, "player barrier header", sourceOffset);
        var count = ReadInt32(data, 0, "player barrier count", sourceOffset);
        if (count < 0)
        {
            throw Invalid(sourceOffset, "player barrier count is negative");
        }

        if (count > (data.Length - 0x10) / 0x10)
        {
            throw Invalid(sourceOffset, $"player barrier count {count} exceeds the group table capacity");
        }

        var tableSize = count * 0x10;
        var ranges = new RangeTracker(data.Length, sourceOffset, "player barriers");
        ranges.Add(0, checked(0x10 + tableSize), "header and group table");
        var barriers = new UyaCollisionPlayerBarrier[count];
        for (var index = 0; index < count; index++)
        {
            var entry = 0x10 + index * 0x10;
            var center = new UyaCollisionVertex(
                ReadUInt16(data, entry, "barrier sphere X", sourceOffset),
                ReadUInt16(data, entry + 2, "barrier sphere Y", sourceOffset),
                ReadUInt16(data, entry + 4, "barrier sphere Z", sourceOffset));
            var radius64 = ReadUInt16(data, entry + 6, "barrier sphere radius", sourceOffset);
            var triangleCount = ReadUInt16(data, entry + 8, "barrier triangle count", sourceOffset);
            var vertexCount = ReadUInt16(data, entry + 10, "barrier vertex count", sourceOffset);
            var dataOffsetValue = ReadUInt32(data, entry + 12, "barrier data offset", sourceOffset);
            if (dataOffsetValue > int.MaxValue)
            {
                throw Invalid(sourceOffset + entry + 12, "barrier data offset exceeds the supported range");
            }

            var dataOffset = (int)dataOffsetValue;
            RequireAligned(dataOffset, 0x10, sourceOffset, "barrier data");
            var dataSize = checked(vertexCount * 8 + triangleCount * 4);
            ranges.Add(dataOffset, dataSize, $"barrier group {index}");
            var vertices = new UyaCollisionVertex[vertexCount];
            var cursor = dataOffset;
            for (var vertex = 0; vertex < vertexCount; vertex++)
            {
                vertices[vertex] = new(
                    ReadUInt16(data, cursor, "barrier vertex X", sourceOffset),
                    ReadUInt16(data, cursor + 2, "barrier vertex Y", sourceOffset),
                    ReadUInt16(data, cursor + 4, "barrier vertex Z", sourceOffset));
                if (ReadUInt16(data, cursor + 6, "barrier vertex padding", sourceOffset) != 0)
                {
                    throw Invalid(sourceOffset + cursor + 6, "barrier vertex padding is nonzero");
                }

                cursor += 8;
            }

            var triangles = new UyaCollisionTriangle[triangleCount];
            for (var triangle = 0; triangle < triangleCount; triangle++)
            {
                var a = data[cursor];
                var b = data[cursor + 1];
                var c = data[cursor + 2];
                ValidateVertexIndex(a, vertexCount, sourceOffset + cursor);
                ValidateVertexIndex(b, vertexCount, sourceOffset + cursor + 1);
                ValidateVertexIndex(c, vertexCount, sourceOffset + cursor + 2);
                if (data[cursor + 3] != 0)
                {
                    throw Invalid(sourceOffset + cursor + 3, "barrier triangle padding is nonzero");
                }

                triangles[triangle] = new(a, b, c);
                cursor += 4;
            }

            barriers[index] = new(index, new(center, radius64), vertices, triangles);
        }

        return barriers;
    }

    private static IReadOnlyList<UyaCollisionSolidPiece> BuildSolidPieces(
        IReadOnlyList<UyaCollisionSolidFace> faces)
    {
        if (faces.Count == 0)
        {
            return [];
        }

        var parents = Enumerable.Range(0, faces.Count).ToArray();
        var edges = new Dictionary<Edge, int>();
        for (var index = 0; index < faces.Count; index++)
        {
            foreach (var edge in GetEdges(faces[index]))
            {
                if (edges.TryGetValue(edge, out var other))
                {
                    Union(parents, index, other);
                }
                else
                {
                    edges.Add(edge, index);
                }
            }
        }

        var groups = new Dictionary<int, List<UyaCollisionSolidFace>>();
        for (var index = 0; index < faces.Count; index++)
        {
            var root = Find(parents, index);
            if (!groups.TryGetValue(root, out var group))
            {
                group = [];
                groups.Add(root, group);
            }

            group.Add(faces[index]);
        }

        var ordered = groups.Values
            .Select(group => group.Order(FaceComparer.Instance).ToArray())
            .OrderBy(group => group[0], FaceComparer.Instance)
            .ToArray();
        return ordered.Select((group, index) => new UyaCollisionSolidPiece(index, group)).ToArray();
    }

    private static IEnumerable<Edge> GetEdges(UyaCollisionSolidFace face)
    {
        yield return new(face.A, face.B);
        yield return new(face.B, face.C);
        if (face.IsQuad)
        {
            yield return new(face.C, face.D);
            yield return new(face.D, face.A);
        }
        else
        {
            yield return new(face.C, face.A);
        }
    }

    internal static UyaCollisionSolidFace Canonicalize(UyaCollisionSolidFace face)
    {
        var vertices = face.IsQuad
            ? new[] { face.A, face.B, face.C, face.D }
            : new[] { face.A, face.B, face.C };
        var best = 0;
        for (var candidate = 1; candidate < vertices.Length; candidate++)
        {
            for (var index = 0; index < vertices.Length; index++)
            {
                var comparison = VertexComparer.Instance.Compare(
                    vertices[(candidate + index) % vertices.Length],
                    vertices[(best + index) % vertices.Length]);
                if (comparison == 0)
                {
                    continue;
                }

                if (comparison < 0)
                {
                    best = candidate;
                }

                break;
            }
        }

        return new(
            face.Type,
            vertices[best],
            vertices[(best + 1) % vertices.Length],
            vertices[(best + 2) % vertices.Length],
            face.IsQuad ? vertices[(best + 3) % vertices.Length] : default,
            face.IsQuad);
    }

    private static int Find(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

    private static void Union(int[] parents, int left, int right)
    {
        var leftRoot = Find(parents, left);
        var rightRoot = Find(parents, right);
        if (leftRoot != rightRoot)
        {
            parents[rightRoot] = leftRoot;
        }
    }

    private static int SignExtend(int value, int bits)
    {
        var shift = 32 - bits;
        return value << shift >> shift;
    }

    private static void ValidateVertexIndex(int index, int count, int offset)
    {
        if (index >= count)
        {
            throw Invalid(offset, $"vertex index {index} exceeds count {count}");
        }
    }

    private static void RequireAligned(int offset, int alignment, int sourceOffset, string name)
    {
        if (offset < 0 || offset % alignment != 0)
        {
            throw Invalid(sourceOffset + Math.Max(offset, 0), $"{name} offset 0x{offset:X} is not {alignment}-byte aligned");
        }
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset, string name, int sourceOffset = 0)
    {
        RequireRange(data, offset, sizeof(short), name, sourceOffset);
        return BinarySpanReader.ReadInt16LittleEndian(data, offset);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, string name, int sourceOffset = 0)
    {
        RequireRange(data, offset, sizeof(ushort), name, sourceOffset);
        return BinarySpanReader.ReadUInt16LittleEndian(data, offset);
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset, string name, int sourceOffset = 0)
    {
        RequireRange(data, offset, sizeof(int), name, sourceOffset);
        return BinarySpanReader.ReadInt32LittleEndian(data, offset);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, string name, int sourceOffset = 0)
    {
        RequireRange(data, offset, sizeof(uint), name, sourceOffset);
        return BinarySpanReader.ReadUInt32LittleEndian(data, offset);
    }

    private static void RequireRange(
        ReadOnlySpan<byte> data,
        int offset,
        int length,
        string name,
        int sourceOffset = 0)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw Invalid(sourceOffset + Math.Max(offset, 0), $"{name} exceeds collision length 0x{data.Length:X}");
        }
    }

    private static InvalidDataException Invalid(int offset, string message) =>
        new($"UYA collision at 0x{offset:X}: {message}.");

    private sealed record SolidReadResult(
        IReadOnlyList<UyaCollisionSolidFace> Faces,
        int OctantCount,
        int NativeFaceCount);

    private readonly record struct Edge
    {
        public Edge(UyaCollisionVertex left, UyaCollisionVertex right)
        {
            if (VertexComparer.Instance.Compare(left, right) <= 0)
            {
                A = left;
                B = right;
            }
            else
            {
                A = right;
                B = left;
            }
        }

        public UyaCollisionVertex A { get; }

        public UyaCollisionVertex B { get; }
    }

    private sealed class VertexComparer : IComparer<UyaCollisionVertex>
    {
        public static VertexComparer Instance { get; } = new();

        public int Compare(UyaCollisionVertex left, UyaCollisionVertex right)
        {
            var x = left.X64.CompareTo(right.X64);
            if (x != 0) return x;
            var y = left.Y64.CompareTo(right.Y64);
            return y != 0 ? y : left.Z64.CompareTo(right.Z64);
        }
    }

    private sealed class FaceComparer : IComparer<UyaCollisionSolidFace>
    {
        public static FaceComparer Instance { get; } = new();

        public int Compare(UyaCollisionSolidFace left, UyaCollisionSolidFace right)
        {
            var result = left.Type.CompareTo(right.Type);
            if (result != 0) return result;
            result = left.IsQuad.CompareTo(right.IsQuad);
            if (result != 0) return result;
            result = VertexComparer.Instance.Compare(left.A, right.A);
            if (result != 0) return result;
            result = VertexComparer.Instance.Compare(left.B, right.B);
            if (result != 0) return result;
            result = VertexComparer.Instance.Compare(left.C, right.C);
            return result != 0 ? result : VertexComparer.Instance.Compare(left.D, right.D);
        }
    }

    private sealed class RangeTracker(int length, int sourceOffset, string owner)
    {
        private readonly List<(int Start, int End, string Name)> _ranges = [];

        public void Add(int offset, int size, string name)
        {
            if (offset < 0 || size < 0 || offset > length - size)
            {
                throw Invalid(sourceOffset + Math.Max(offset, 0), $"{owner} {name} exceeds its 0x{length:X}-byte region");
            }

            var end = offset + size;
            foreach (var range in _ranges)
            {
                if (offset < range.End && end > range.Start)
                {
                    throw Invalid(sourceOffset + offset, $"{owner} {name} overlaps {range.Name}");
                }
            }

            _ranges.Add((offset, end, name));
        }
    }
}
