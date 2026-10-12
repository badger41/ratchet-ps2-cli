using System.Security.Cryptography;

namespace RatchetPs2.Core.Tfrags;

public static class TfragPieceCanonicalCodec
{
    public const uint FormatVersion = 1;
    public const int MaximumCanonicalBytes = 64 * 1024 * 1024;

    private const uint Magic = 0x30435054; // TPC0
    private const int LodCount = 3;
    private const int MaximumPrimitivesPerLod = 4096;
    private const int VertexByteLength = 84;

    public static byte[] Write(TfragPieceGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ValidateGeometry(geometry);
        var byteLength = CalculateByteLength(geometry);
        if (byteLength > MaximumCanonicalBytes)
        {
            throw new InvalidDataException(
                $"Canonical tfrag piece is {byteLength} bytes; the limit is {MaximumCanonicalBytes} bytes.");
        }

        using var stream = new MemoryStream(checked((int)byteLength));
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        WriteBounds(writer, geometry.Bounds);
        writer.Write(geometry.TextureIds.Count);
        foreach (var textureId in geometry.TextureIds)
        {
            writer.Write(textureId);
        }

        writer.Write(geometry.Lods.Count);
        foreach (var lod in geometry.Lods)
        {
            writer.Write(lod.LodIndex);
            writer.Write(lod.Primitives.Count);
            foreach (var primitive in lod.Primitives)
            {
                writer.Write(primitive.Material.TextureId);
                writer.Write((primitive.Material.ClampU ? 1 : 0) | (primitive.Material.ClampV ? 2 : 0));
                writer.Write(primitive.Vertices.Count);
                writer.Write(primitive.Indices.Count);
                foreach (var vertex in primitive.Vertices)
                {
                    WriteVertex(writer, vertex);
                }

                foreach (var index in primitive.Indices)
                {
                    writer.Write(index);
                }
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static TfragPieceGeometry Read(byte[] canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(canonicalBytes);
        if (canonicalBytes.Length > MaximumCanonicalBytes)
        {
            throw new InvalidDataException(
                $"Canonical tfrag piece is {canonicalBytes.Length} bytes; the limit is {MaximumCanonicalBytes} bytes.");
        }

        try
        {
            using var stream = new MemoryStream(canonicalBytes, writable: false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic)
            {
                throw new InvalidDataException("Canonical tfrag piece has an invalid magic value.");
            }

            var version = reader.ReadUInt32();
            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Canonical tfrag piece version {version} is unsupported.");
            }

            var bounds = ReadBounds(reader);
            var textureCount = ReadCount(reader, canonicalBytes.Length / sizeof(int), "texture dependency");
            EnsureRemaining(stream, checked((long)textureCount * sizeof(int)), "texture dependencies");
            var textureIds = new int[textureCount];
            for (var index = 0; index < textureIds.Length; index++)
            {
                textureIds[index] = reader.ReadInt32();
            }

            var lodCount = ReadCount(reader, LodCount, "LOD");
            if (lodCount != LodCount)
            {
                throw new InvalidDataException($"Canonical tfrag piece must contain {LodCount} LODs, got {lodCount}.");
            }

            var lods = new TfragPieceLod[lodCount];
            for (var lodOffset = 0; lodOffset < lodCount; lodOffset++)
            {
                var lodIndex = reader.ReadInt32();
                var primitiveCount = ReadCount(reader, MaximumPrimitivesPerLod, $"LOD {lodIndex} primitive");
                var primitives = new TfragPiecePrimitive[primitiveCount];
                for (var primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
                {
                    var textureId = reader.ReadInt32();
                    var flags = reader.ReadInt32();
                    if ((flags & ~3) != 0)
                    {
                        throw new InvalidDataException(
                            $"Canonical tfrag LOD {lodIndex} primitive {primitiveIndex} has invalid material flags {flags}.");
                    }

                    var vertexCount = ReadCount(reader, canonicalBytes.Length / VertexByteLength,
                        $"LOD {lodIndex} primitive {primitiveIndex} vertex");
                    var indexCount = ReadCount(reader, canonicalBytes.Length / sizeof(uint),
                        $"LOD {lodIndex} primitive {primitiveIndex} index");
                    EnsureRemaining(stream, checked((long)vertexCount * VertexByteLength + (long)indexCount * sizeof(uint)),
                        $"LOD {lodIndex} primitive {primitiveIndex}");

                    var vertices = new TfragPieceVertex[vertexCount];
                    for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                    {
                        vertices[vertexIndex] = ReadVertex(reader);
                    }

                    var indices = new uint[indexCount];
                    for (var index = 0; index < indexCount; index++)
                    {
                        indices[index] = reader.ReadUInt32();
                    }

                    primitives[primitiveIndex] = new TfragPiecePrimitive(
                        new TfragPieceMaterial(textureId, (flags & 1) != 0, (flags & 2) != 0),
                        vertices,
                        indices);
                }

                lods[lodOffset] = new TfragPieceLod(lodIndex, primitives);
            }

            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException(
                    $"Canonical tfrag piece has {stream.Length - stream.Position} trailing byte(s).");
            }

            var geometry = new TfragPieceGeometry(bounds, textureIds, lods);
            ValidateGeometry(geometry);
            return geometry;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Canonical tfrag piece is truncated.", exception);
        }
    }

    public static string ComputeContentId(ReadOnlySpan<byte> canonicalBytes) =>
        Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();

    private static void ValidateGeometry(TfragPieceGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry.Bounds);
        ArgumentNullException.ThrowIfNull(geometry.TextureIds);
        ArgumentNullException.ThrowIfNull(geometry.Lods);
        ValidateBounds(geometry.Bounds);
        if (geometry.TextureIds.Count > MaximumCanonicalBytes / sizeof(int)
            || geometry.TextureIds.Any(textureId => textureId < 0)
            || !geometry.TextureIds.SequenceEqual(geometry.TextureIds.Distinct().Order()))
        {
            throw new InvalidDataException("Canonical tfrag texture dependencies must be sorted, unique, and non-negative.");
        }

        if (geometry.Lods.Count != LodCount
            || !geometry.Lods.Select(lod => lod.LodIndex).SequenceEqual(Enumerable.Range(0, LodCount)))
        {
            throw new InvalidDataException("Canonical tfrag piece must contain LODs 0, 1, and 2 in order.");
        }

        var materialTextureIds = new HashSet<int>();
        foreach (var lod in geometry.Lods)
        {
            ArgumentNullException.ThrowIfNull(lod.Primitives);
            if (lod.Primitives.Count > MaximumPrimitivesPerLod)
            {
                throw new InvalidDataException(
                    $"Canonical tfrag LOD {lod.LodIndex} has {lod.Primitives.Count} primitives; the limit is {MaximumPrimitivesPerLod}.");
            }

            foreach (var primitive in lod.Primitives)
            {
                ArgumentNullException.ThrowIfNull(primitive.Material);
                ArgumentNullException.ThrowIfNull(primitive.Vertices);
                ArgumentNullException.ThrowIfNull(primitive.Indices);
                if (primitive.Material.TextureId >= 0)
                {
                    materialTextureIds.Add(primitive.Material.TextureId);
                }

                if (primitive.Indices.Count % 3 != 0
                    || primitive.Indices.Any(index => index >= (uint)primitive.Vertices.Count))
                {
                    throw new InvalidDataException(
                        $"Canonical tfrag LOD {lod.LodIndex} contains invalid triangle indices.");
                }

                foreach (var vertex in primitive.Vertices)
                {
                    ValidateVertex(vertex);
                }
            }
        }

        if (!geometry.TextureIds.SequenceEqual(materialTextureIds.Order()))
        {
            throw new InvalidDataException("Canonical tfrag texture dependencies do not match its materials.");
        }
    }

    private static void WriteBounds(BinaryWriter writer, TfragPieceBounds bounds)
    {
        WriteVector3(writer, bounds.Minimum);
        WriteVector3(writer, bounds.Maximum);
        writer.Write(Canonical(bounds.Radius));
    }

    private static TfragPieceBounds ReadBounds(BinaryReader reader) =>
        new(ReadVector3(reader), ReadVector3(reader), reader.ReadSingle());

    private static void WriteVertex(BinaryWriter writer, TfragPieceVertex vertex)
    {
        WriteVector3(writer, vertex.Position);
        WriteVector3(writer, vertex.Normal);
        WriteVector2(writer, vertex.TexCoord);
        WriteVector4(writer, vertex.Color);
        writer.Write(Canonical(vertex.LightSelector));
        WriteVector4(writer, vertex.LightBaseColor);
        WriteVector3(writer, vertex.LightNormal);
        writer.Write(Canonical(vertex.LightPostScale));
    }

    private static TfragPieceVertex ReadVertex(BinaryReader reader) => new(
        ReadVector3(reader),
        ReadVector3(reader),
        ReadVector2(reader),
        ReadVector4(reader),
        reader.ReadSingle(),
        ReadVector4(reader),
        ReadVector3(reader),
        reader.ReadSingle());

    private static void WriteVector2(BinaryWriter writer, TfragPieceVector2 value)
    {
        writer.Write(Canonical(value.X));
        writer.Write(Canonical(value.Y));
    }

    private static void WriteVector3(BinaryWriter writer, TfragPieceVector3 value)
    {
        writer.Write(Canonical(value.X));
        writer.Write(Canonical(value.Y));
        writer.Write(Canonical(value.Z));
    }

    private static void WriteVector4(BinaryWriter writer, TfragPieceVector4 value)
    {
        writer.Write(Canonical(value.X));
        writer.Write(Canonical(value.Y));
        writer.Write(Canonical(value.Z));
        writer.Write(Canonical(value.W));
    }

    private static TfragPieceVector2 ReadVector2(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle());

    private static TfragPieceVector3 ReadVector3(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static TfragPieceVector4 ReadVector4(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void ValidateBounds(TfragPieceBounds bounds)
    {
        if (!IsFinite(bounds.Minimum)
            || !IsFinite(bounds.Maximum)
            || !float.IsFinite(bounds.Radius)
            || bounds.Minimum.X > bounds.Maximum.X
            || bounds.Minimum.Y > bounds.Maximum.Y
            || bounds.Minimum.Z > bounds.Maximum.Z
            || bounds.Radius < 0)
        {
            throw new InvalidDataException("Canonical tfrag bounds are invalid.");
        }
    }

    private static void ValidateVertex(TfragPieceVertex vertex)
    {
        if (!IsFinite(vertex.Position)
            || !IsFinite(vertex.Normal)
            || !IsFinite(vertex.TexCoord)
            || !IsFinite(vertex.Color)
            || !float.IsFinite(vertex.LightSelector)
            || !IsFinite(vertex.LightBaseColor)
            || !IsFinite(vertex.LightNormal)
            || !float.IsFinite(vertex.LightPostScale))
        {
            throw new InvalidDataException("Canonical tfrag geometry contains a non-finite value.");
        }
    }

    private static bool IsFinite(TfragPieceVector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(TfragPieceVector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(TfragPieceVector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static long CalculateByteLength(TfragPieceGeometry geometry)
    {
        var byteLength = checked(8L + 28 + 4 + (long)geometry.TextureIds.Count * sizeof(int) + 4);
        foreach (var lod in geometry.Lods)
        {
            byteLength = checked(byteLength + 8);
            foreach (var primitive in lod.Primitives)
            {
                byteLength = checked(byteLength + 16
                    + (long)primitive.Vertices.Count * VertexByteLength
                    + (long)primitive.Indices.Count * sizeof(uint));
            }
        }

        return byteLength;
    }

    private static float Canonical(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException("Canonical tfrag geometry contains a non-finite value.");
        }

        var rounded = MathF.Round(value, 6, MidpointRounding.ToEven);
        return rounded == 0 ? 0 : rounded;
    }

    private static int ReadCount(BinaryReader reader, int maximum, string label)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > maximum)
        {
            throw new InvalidDataException($"Canonical tfrag {label} count {count} is invalid.");
        }

        return count;
    }

    private static void EnsureRemaining(Stream stream, long byteCount, string label)
    {
        if (byteCount < 0 || byteCount > stream.Length - stream.Position)
        {
            throw new InvalidDataException($"Canonical tfrag {label} exceeds the payload bounds.");
        }
    }
}
