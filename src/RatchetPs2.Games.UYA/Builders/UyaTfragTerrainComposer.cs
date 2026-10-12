using System.Buffers.Binary;
using System.Numerics;

using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaTfragTerrainComposer
{
    private const float PositionTolerance = 0.002f;
    private const float LightNormalTolerance = 0.04f;
    private const float ScalarTolerance = 0.00001f;

    public static TfragPieceComposition Compose(
        byte[] sourceBytes,
        IReadOnlyList<TfragPieceCompositionEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(edits);
        cancellationToken.ThrowIfCancellationRequested();
        if (edits.Count == 0) return new(sourceBytes, 0);
        var terrain = TfragTerrainReader.Read(sourceBytes);
        if (edits.Count > terrain.Chunks.Count
            || edits.Any(edit => edit is null || string.IsNullOrWhiteSpace(edit.Id)
                || edit.CanonicalBytes is null || edit.GroupIndex < 0)
            || edits.Select(edit => edit.Id).Distinct(StringComparer.Ordinal).Count() != edits.Count
            || edits.Select(edit => edit.GroupIndex).Distinct().Count() != edits.Count
            || edits.Any(edit => edit.GroupIndex >= terrain.Chunks.Count))
            throw new InvalidDataException("Tfrag composition edits do not match the source group inventory.");

        byte[]? output = null;
        var changed = 0;
        var sourceGeometries = new Dictionary<int, TfragPieceGeometry>();
        foreach (var edit in edits.OrderBy(edit => edit.GroupIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = terrain.Chunks[edit.GroupIndex];
            var sourceGeometry = TfragGltfExporter.DecodeCanonicalPiece(terrain, chunk, cancellationToken);
            sourceGeometries.Add(edit.GroupIndex, sourceGeometry);
            if (!TfragPieceCanonicalCodec.Write(sourceGeometry).AsSpan().SequenceEqual(edit.CanonicalBytes))
                throw new InvalidDataException($"Tfrag {edit.Id} canonical asset does not match source group {edit.GroupIndex}.");
            TfragPieceTransformer.Validate(sourceGeometry.Bounds, edit.Transform);
            if (Unchanged(chunk, edit.Transform)) continue;

            output ??= sourceBytes.ToArray();
            Apply(output, terrain, chunk, edit, cancellationToken);
            changed++;
        }

        if (output is null) return new(sourceBytes, 0);
        var reread = TfragTerrainReader.Read(output);
        foreach (var edit in edits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceChunk = terrain.Chunks[edit.GroupIndex];
            var actualChunk = reread.Chunks[edit.GroupIndex];
            var expected = TfragPieceTransformer.Transform(
                sourceGeometries[edit.GroupIndex],
                edit.Transform with { Position = new(0, 0, 0) },
                cancellationToken);
            var actual = TfragGltfExporter.DecodeCanonicalPiece(reread, actualChunk, cancellationToken);
            ValidateGeometry(edit.Id, expected, actual);
            Near(edit.Transform.Position.X * 1024f, actualChunk.BoundingSphere.X, PositionTolerance * 1024f,
                edit.Id, "group sphere X");
            Near(edit.Transform.Position.Y * 1024f, actualChunk.BoundingSphere.Y, PositionTolerance * 1024f,
                edit.Id, "group sphere Y");
            Near(edit.Transform.Position.Z * 1024f, actualChunk.BoundingSphere.Z, PositionTolerance * 1024f,
                edit.Id, "group sphere Z");
            Near(sourceChunk.BoundingSphere.Radius * edit.Transform.Scale.X,
                actualChunk.BoundingSphere.Radius, PositionTolerance * 1024f, edit.Id, "group sphere radius");
        }
        return new(output, changed);
    }

    private static void Apply(
        byte[] output,
        TfragTerrain terrain,
        TfragChunk chunk,
        TfragPieceCompositionEdit edit,
        CancellationToken cancellationToken)
    {
        var rotation = Normalize(edit.Transform.Rotation);
        var scale = edit.Transform.Scale.X;
        var sourceCenter = new Vector3(chunk.BoundingSphere.X, chunk.BoundingSphere.Y, chunk.BoundingSphere.Z);
        var targetCenter = Vector(edit.Transform.Position) * 1024f;
        var packets = TfragGltfExporter.FindNativePositionPackets(terrain, chunk);
        if (packets.Sum(packet => packet.RowCount) != chunk.VertexCount)
            throw new InvalidDataException($"Tfrag {edit.Id} native position inventory is incomplete.");
        foreach (var packet in packets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var row = 0; row < packet.RowCount; row++)
            {
                var offset = packet.Offset + 4 + row * 6;
                var local = new Vector3(
                    BinaryPrimitives.ReadInt16LittleEndian(terrain.SourceBytes.Span.Slice(offset, 2)),
                    BinaryPrimitives.ReadInt16LittleEndian(terrain.SourceBytes.Span.Slice(offset + 2, 2)),
                    BinaryPrimitives.ReadInt16LittleEndian(terrain.SourceBytes.Span.Slice(offset + 4, 2)));
                var transformed = Vector3.Transform(local * scale, rotation);
                WriteInt16(output, offset, transformed.X, edit.Id);
                WriteInt16(output, offset + 2, transformed.Y, edit.Id);
                WriteInt16(output, offset + 4, transformed.Z, edit.Id);
            }
        }

        foreach (var packet in packets.Where(packet => packet.UsesVifBase)
                     .DistinctBy(packet => packet.BaseCommandOffset))
        {
            if (packet.BaseCommandOffset < 0)
                throw new InvalidDataException($"Tfrag {edit.Id} has a position packet without its VIF base command.");
            var sourceBase = new Vector3(packet.BaseX, packet.BaseY, packet.BaseZ);
            var transformedBase = targetCenter
                + Vector3.Transform((sourceBase - sourceCenter) * scale, rotation);
            var offset = packet.BaseCommandOffset + 4;
            WriteInt32(output, offset, transformedBase.X, edit.Id);
            WriteInt32(output, offset + 4, transformedBase.Y, edit.Id);
            WriteInt32(output, offset + 8, transformedBase.Z, edit.Id);
        }

        PatchLightNormals(output, terrain.SourceBytes.Span, chunk, rotation, edit.Id);
        PatchMeshSpheres(output, terrain.SourceBytes.Span, chunk, sourceCenter, targetCenter, rotation, scale, edit.Id);
        PatchCube(output, terrain.SourceBytes.Span, chunk,
            sourceCenter, targetCenter, rotation, scale, edit.Id);
        WriteSingle(output, chunk.RecordOffset, targetCenter.X, edit.Id);
        WriteSingle(output, chunk.RecordOffset + 4, targetCenter.Y, edit.Id);
        WriteSingle(output, chunk.RecordOffset + 8, targetCenter.Z, edit.Id);
        WriteSingle(output, chunk.RecordOffset + 12, chunk.BoundingSphere.Radius * scale, edit.Id);
    }

    private static void PatchLightNormals(
        byte[] output,
        ReadOnlySpan<byte> source,
        TfragChunk chunk,
        Quaternion rotation,
        string id)
    {
        if (chunk.VertexCount == 0) return;
        if (chunk.LightOffset < 0)
            throw new InvalidDataException($"Tfrag {id} has vertices without a light table.");
        var offset = checked(chunk.DataOffset + chunk.LightOffset + 0x10);
        var length = checked(chunk.VertexCount * 8);
        EnsureRange(source, offset, length, id, "light rows");
        for (var row = 0; row < chunk.VertexCount; row++)
        {
            var valueOffset = offset + row * 8 + 2;
            var normal = DecodeLightNormal(BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(valueOffset, 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                output.AsSpan(valueOffset, 2), EncodeLightNormal(Vector3.Transform(normal, rotation)));
        }
    }

    private static void PatchMeshSpheres(
        byte[] output,
        ReadOnlySpan<byte> source,
        TfragChunk chunk,
        Vector3 sourceCenter,
        Vector3 targetCenter,
        Quaternion rotation,
        float scale,
        string id)
    {
        if (chunk.MSphereCount == 0) return;
        if (chunk.MSphereOffset < 0)
            throw new InvalidDataException($"Tfrag {id} has mesh spheres without a sphere table.");
        var offset = checked(chunk.DataOffset + chunk.MSphereOffset);
        var length = checked(chunk.MSphereCount * 16);
        EnsureRange(source, offset, length, id, "mesh spheres");
        for (var index = 0; index < chunk.MSphereCount; index++)
        {
            var sphere = offset + index * 16;
            var center = new Vector3(
                BinaryPrimitives.ReadSingleLittleEndian(source.Slice(sphere, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(source.Slice(sphere + 4, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(source.Slice(sphere + 8, 4)));
            var transformed = targetCenter + Vector3.Transform((center - sourceCenter) * scale, rotation);
            WriteSingle(output, sphere, transformed.X, id);
            WriteSingle(output, sphere + 4, transformed.Y, id);
            WriteSingle(output, sphere + 8, transformed.Z, id);
            var radius = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(sphere + 12, 2));
            WriteUInt16(output, sphere + 12, radius * scale, id);
        }
    }

    private static void PatchCube(
        byte[] output,
        ReadOnlySpan<byte> source,
        TfragChunk chunk,
        Vector3 sourceCenter,
        Vector3 targetCenter,
        Quaternion rotation,
        float scale,
        string id)
    {
        const int cubeVertexCount = 8;
        const int cubeVertexSize = 8;
        if (chunk.CubeOffset < 0) return;
        var offset = checked(chunk.DataOffset + chunk.CubeOffset);
        EnsureRange(source, offset, cubeVertexCount * cubeVertexSize, id, "bounding cube");
        for (var index = 0; index < cubeVertexCount; index++)
        {
            var vertex = offset + index * cubeVertexSize;
            var world = new Vector3(
                BinaryPrimitives.ReadInt16LittleEndian(source.Slice(vertex, 2)) * 64f,
                BinaryPrimitives.ReadInt16LittleEndian(source.Slice(vertex + 2, 2)) * 64f,
                BinaryPrimitives.ReadInt16LittleEndian(source.Slice(vertex + 4, 2)) * 64f);
            var transformed = targetCenter + Vector3.Transform((world - sourceCenter) * scale, rotation);
            WriteInt16(output, vertex, transformed.X / 64f, id);
            WriteInt16(output, vertex + 2, transformed.Y / 64f, id);
            WriteInt16(output, vertex + 4, transformed.Z / 64f, id);
        }
    }

    private static void ValidateGeometry(string id, TfragPieceGeometry expected, TfragPieceGeometry actual)
    {
        if (!expected.TextureIds.SequenceEqual(actual.TextureIds)
            || expected.Lods.Count != actual.Lods.Count)
            throw ValidationError(id, "texture or LOD inventory");
        Near(expected.Bounds.Minimum, actual.Bounds.Minimum, PositionTolerance, id, "bounds minimum");
        Near(expected.Bounds.Maximum, actual.Bounds.Maximum, PositionTolerance, id, "bounds maximum");
        Near(expected.Bounds.Radius, actual.Bounds.Radius, PositionTolerance, id, "bounds radius");
        for (var lod = 0; lod < expected.Lods.Count; lod++)
        {
            var expectedLod = expected.Lods[lod];
            var actualLod = actual.Lods[lod];
            if (expectedLod.LodIndex != actualLod.LodIndex
                || expectedLod.Primitives.Count != actualLod.Primitives.Count)
                throw ValidationError(id, "LOD inventory");
            for (var primitive = 0; primitive < expectedLod.Primitives.Count; primitive++)
            {
                var left = expectedLod.Primitives[primitive];
                var right = actualLod.Primitives[primitive];
                if (left.Material != right.Material)
                    throw ValidationError(id, $"LOD {lod} primitive {primitive} material");
                if (!TrianglesEquivalent(left, right))
                    throw ValidationError(id,
                        $"LOD {lod} primitive {primitive} topology "
                        + $"({left.Vertices.Count}/{left.Indices.Count} expected vertices/indices, "
                        + $"{right.Vertices.Count}/{right.Indices.Count} actual)");
            }
        }
    }

    internal static bool TrianglesEquivalent(TfragPiecePrimitive expected, TfragPiecePrimitive actual)
    {
        if (expected.Indices.Count != actual.Indices.Count
            || expected.Indices.Count % 3 != 0)
            return false;

        var matched = new bool[actual.Indices.Count / 3];
        for (var expectedIndex = 0; expectedIndex < expected.Indices.Count; expectedIndex += 3)
        {
            var found = false;
            for (var actualIndex = 0; actualIndex < actual.Indices.Count; actualIndex += 3)
            {
                var triangle = actualIndex / 3;
                if (matched[triangle]
                    || !TriangleEquivalent(expected, expectedIndex, actual, actualIndex)) continue;
                matched[triangle] = true;
                found = true;
                break;
            }
            if (!found) return false;
        }
        return true;
    }

    private static bool TriangleEquivalent(
        TfragPiecePrimitive expected,
        int expectedIndex,
        TfragPiecePrimitive actual,
        int actualIndex)
    {
        for (var start = 0; start < 3; start++)
        {
            if (TriangleOrderEquivalent(expected, expectedIndex, actual, actualIndex, start))
                return true;
        }
        return false;
    }

    private static bool TriangleOrderEquivalent(
        TfragPiecePrimitive expected,
        int expectedIndex,
        TfragPiecePrimitive actual,
        int actualIndex,
        int actualStart)
    {
        for (var vertex = 0; vertex < 3; vertex++)
        {
            var leftIndex = checked((int)expected.Indices[expectedIndex + vertex]);
            var rightIndex = checked((int)actual.Indices[
                actualIndex + (actualStart + vertex) % 3]);
            if (leftIndex < 0 || leftIndex >= expected.Vertices.Count
                || rightIndex < 0 || rightIndex >= actual.Vertices.Count
                || !VertexEquivalent(expected.Vertices[leftIndex], actual.Vertices[rightIndex]))
                return false;
        }
        return true;
    }

    private static bool VertexEquivalent(TfragPieceVertex left, TfragPieceVertex right) =>
        Within(left.Position, right.Position, PositionTolerance)
        // Surface normals are regenerated from quantized positions and can drift at small scales.
        && Within(left.LightNormal, right.LightNormal, LightNormalTolerance)
        && Within(left.Color, right.Color, ScalarTolerance)
        && Within(left.LightBaseColor, right.LightBaseColor, ScalarTolerance)
        && Within(left.TexCoord.X, right.TexCoord.X, ScalarTolerance)
        && Within(left.TexCoord.Y, right.TexCoord.Y, ScalarTolerance)
        && Within(left.LightSelector, right.LightSelector, ScalarTolerance)
        && Within(left.LightPostScale, right.LightPostScale, ScalarTolerance);

    private static bool Unchanged(TfragChunk chunk, TfragPieceTransform transform) =>
        transform.Position == new TfragPieceVector3(
            chunk.BoundingSphere.X / 1024f,
            chunk.BoundingSphere.Y / 1024f,
            chunk.BoundingSphere.Z / 1024f)
        && SameRotation(transform.Rotation, new(0, 0, 0, 1))
        && transform.Scale == new TfragPieceVector3(1, 1, 1);

    private static bool SameRotation(TfragPieceVector4 left, TfragPieceVector4 right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        return MathF.Abs(MathF.Abs(Quaternion.Dot(a, b)) - 1) <= ScalarTolerance;
    }

    private static Vector3 DecodeLightNormal(ushort packed)
    {
        const float step = 2f * MathF.PI / 256f;
        var azimuth = (packed & 0xFF) * step;
        var elevation = (packed >> 8) * step;
        return Vector3.Normalize(new(
            MathF.Cos(azimuth) * MathF.Cos(elevation),
            MathF.Sin(azimuth) * MathF.Cos(elevation),
            MathF.Sin(elevation)));
    }

    private static ushort EncodeLightNormal(Vector3 value)
    {
        var normal = value.LengthSquared() > 0 ? Vector3.Normalize(value) : Vector3.UnitZ;
        var azimuth = MathF.Atan2(normal.Y, normal.X);
        var elevation = MathF.Asin(Math.Clamp(normal.Z, -1, 1));
        return (ushort)(AngleByte(azimuth) | AngleByte(elevation) << 8);
    }

    private static int AngleByte(float value)
    {
        var turns = value / (2f * MathF.PI);
        return ((int)MathF.Round(turns * 256f, MidpointRounding.ToEven) % 256 + 256) % 256;
    }

    private static Quaternion Normalize(TfragPieceVector4 value) =>
        Quaternion.Normalize(new(value.X, value.Y, value.Z, value.W));

    private static Vector3 Vector(TfragPieceVector3 value) => new(value.X, value.Y, value.Z);

    private static void EnsureRange(ReadOnlySpan<byte> source, int offset, int length, string id, string field)
    {
        if (offset < 0 || length < 0 || offset > source.Length - length)
            throw new InvalidDataException($"Tfrag {id} {field} exceed the source group.");
    }

    private static void WriteInt16(byte[] output, int offset, float value, string id)
    {
        var rounded = MathF.Round(value, MidpointRounding.ToEven);
        if (!float.IsFinite(rounded) || rounded is < short.MinValue or > short.MaxValue)
            throw new InvalidDataException($"Tfrag {id} transformed position exceeds signed 16-bit storage.");
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset, 2), (short)rounded);
    }

    private static void WriteUInt16(byte[] output, int offset, float value, string id)
    {
        var rounded = MathF.Round(value, MidpointRounding.ToEven);
        if (!float.IsFinite(rounded) || rounded is < ushort.MinValue or > ushort.MaxValue)
            throw new InvalidDataException($"Tfrag {id} transformed radius exceeds unsigned 16-bit storage.");
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset, 2), (ushort)rounded);
    }

    private static void WriteInt32(byte[] output, int offset, float value, string id)
    {
        var rounded = MathF.Round(value, MidpointRounding.ToEven);
        if (!float.IsFinite(rounded) || rounded is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException($"Tfrag {id} transformed base exceeds signed 32-bit storage.");
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset, 4), (int)rounded);
    }

    private static void WriteSingle(byte[] output, int offset, float value, string id)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"Tfrag {id} transformed bounds are not finite.");
        BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(offset, 4), value);
    }

    private static void Near(
        TfragPieceVector3 expected,
        TfragPieceVector3 actual,
        float tolerance,
        string id,
        string field)
    {
        if (MathF.Abs(expected.X - actual.X) <= tolerance
            && MathF.Abs(expected.Y - actual.Y) <= tolerance
            && MathF.Abs(expected.Z - actual.Z) <= tolerance) return;
        throw ValidationError(id, field, expected, actual);
    }

    private static bool Within(TfragPieceVector3 left, TfragPieceVector3 right, float tolerance) =>
        Within(left.X, right.X, tolerance)
        && Within(left.Y, right.Y, tolerance)
        && Within(left.Z, right.Z, tolerance);

    private static bool Within(TfragPieceVector4 left, TfragPieceVector4 right, float tolerance) =>
        Within(left.X, right.X, tolerance)
        && Within(left.Y, right.Y, tolerance)
        && Within(left.Z, right.Z, tolerance)
        && Within(left.W, right.W, tolerance);

    private static bool Within(float left, float right, float tolerance) =>
        MathF.Abs(left - right) <= tolerance;

    private static void Near(
        TfragPieceVector4 expected,
        TfragPieceVector4 actual,
        float tolerance,
        string id,
        string field)
    {
        if (MathF.Abs(expected.X - actual.X) <= tolerance
            && MathF.Abs(expected.Y - actual.Y) <= tolerance
            && MathF.Abs(expected.Z - actual.Z) <= tolerance
            && MathF.Abs(expected.W - actual.W) <= tolerance) return;
        throw ValidationError(id, field, expected, actual);
    }

    private static void Near(float expected, float actual, float tolerance, string id, string field)
    {
        if (MathF.Abs(expected - actual) <= tolerance) return;
        throw ValidationError(id, field, expected, actual);
    }

    private static InvalidDataException ValidationError(string id, string field) =>
        new($"Tfrag {id} failed semantic re-read validation for {field}.");

    private static InvalidDataException ValidationError<T>(string id, string field, T expected, T actual) =>
        new($"Tfrag {id} failed semantic re-read validation for {field}: expected {expected}, got {actual}.");
}
