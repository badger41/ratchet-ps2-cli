using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.IO.Vif;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static partial class UyaStaticModelTfragWriter
{
    private static byte[] TexturePayload(IReadOnlyList<TfragPieceMaterial> materials)
    {
        var payload = new byte[checked(materials.Count * 0x50)];
        for (var index = 0; index < materials.Count; index++)
        {
            var offset = index * 0x50;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), materials[index].TextureId);
            payload[offset + 8] = 0x06;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 0x10), 0x0000FF77);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 0x14), 4);
            payload[offset + 0x18] = 0x14;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 0x1C), 0x45000000);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 0x20), materials[index].ClampU ? 1 : 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 0x24), materials[index].ClampV ? 1 : 0);
            payload[offset + 0x28] = 0x08;
            payload[offset + 0x38] = 0x34;
            payload[offset + 0x48] = 0x36;
        }
        return payload;
    }

    private static byte[] VertexInfoPayload(IReadOnlyList<TfragPieceVertex> vertices)
    {
        var payload = new byte[checked(vertices.Count * 8)];
        for (var index = 0; index < vertices.Count; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(index * 8), QuantizeUv(vertices[index].TexCoord.X));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(index * 8 + 2), QuantizeUv(vertices[index].TexCoord.Y));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(index * 8 + 4), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(index * 8 + 6), checked((short)(index * 2)));
        }
        return payload;
    }

    private static byte[] PositionPayload(IReadOnlyList<TfragPieceVertex> vertices)
    {
        var payload = new byte[checked(vertices.Count * 6)];
        for (var index = 0; index < vertices.Count; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(index * 6), QuantizeInt16(vertices[index].Position.X * 1024f, "position X"));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(index * 6 + 2), QuantizeInt16(vertices[index].Position.Y * 1024f, "position Y"));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(index * 6 + 4), QuantizeInt16(vertices[index].Position.Z * 1024f, "position Z"));
        }
        return payload;
    }

    private static byte[] StripPayload(IReadOnlyList<StripRow> rows)
    {
        var payload = new byte[rows.Count * 4];
        for (var index = 0; index < rows.Count; index++)
        {
            payload[index * 4] = unchecked((byte)rows[index].Count);
            payload[index * 4 + 1] = unchecked((byte)rows[index].Control);
            payload[index * 4 + 2] = unchecked((byte)rows[index].TextureOffset);
            payload[index * 4 + 3] = unchecked((byte)rows[index].Padding);
        }
        return payload;
    }

    private static byte[] SingleVertexInfoRow(int positionAddress) =>
        IntVectorPayload(0x45000000, 0x45000000, 0, positionAddress);

    private static byte[] IntVectorPayload(int value) => IntVectorPayload(value, value, value, value);

    private static byte[] IntVectorPayload(int x, int y, int z, int w)
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(payload, x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), y);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), z);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), w);
        return payload;
    }

    private static byte[] VectorPayload(Vector4 value) =>
        IntVectorPayload(checked((int)value.X), checked((int)value.Y), checked((int)value.Z), checked((int)value.W));

    private static void WriteColor(Stream stream, TfragPieceVector4 value)
    {
        stream.WriteByte(QuantizeByte(value.X, 128));
        stream.WriteByte(QuantizeByte(value.Y, 128));
        stream.WriteByte(QuantizeByte(value.Z, 128));
        stream.WriteByte(QuantizeByte(value.W, Ps2Color.FullOpacityAlpha));
    }

    private static void WriteLight(Stream stream, TfragPieceVertex vertex)
    {
        stream.WriteByte(0);
        stream.WriteByte(QuantizeByte(vertex.LightPostScale, 128));
        Span<byte> row = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(row, EncodeNormal(vertex.LightNormal));
        BinaryPrimitives.WriteUInt16LittleEndian(row[2..], EncodeColor5(vertex.LightBaseColor));
        BinaryPrimitives.WriteUInt16LittleEndian(row[4..], checked((ushort)MathF.Round(vertex.LightSelector)));
        stream.Write(row);
    }

    private static void WriteMeshSpheres(
        Stream stream,
        TfragPieceVector3 origin,
        IReadOnlyList<MeshSphere> spheres)
    {
        Span<byte> value = stackalloc byte[16];
        value[14] = byte.MaxValue;
        foreach (var sphere in spheres)
        {
            var center = (sphere.Center + new Vector3(origin.X, origin.Y, origin.Z)) * 1024f;
            var radius = MathF.Ceiling(sphere.Radius * 1024f);
            if (!float.IsFinite(radius) || radius is < 0 or > ushort.MaxValue)
                throw new InvalidDataException("Custom tfrag mesh sphere radius exceeds unsigned 16-bit storage.");
            BinaryPrimitives.WriteSingleLittleEndian(value, center.X);
            BinaryPrimitives.WriteSingleLittleEndian(value[4..], center.Y);
            BinaryPrimitives.WriteSingleLittleEndian(value[8..], center.Z);
            BinaryPrimitives.WriteUInt16LittleEndian(value[12..], (ushort)radius);
            value[15] = sphere.TextureId;
            stream.Write(value);
        }
    }

    private static void WriteCube(Stream stream, TfragPieceVector3 origin, TfragPieceBounds bounds)
    {
        var xs = new[] { bounds.Minimum.X, bounds.Maximum.X };
        var ys = new[] { bounds.Minimum.Y, bounds.Maximum.Y };
        var zs = new[] { bounds.Minimum.Z, bounds.Maximum.Z };
        var value = new byte[8];
        foreach (var z in zs)
        foreach (var y in ys)
        foreach (var x in xs)
        {
            BinaryPrimitives.WriteInt16LittleEndian(value, QuantizeInt16((origin.X + x) * 16f, "cube X"));
            BinaryPrimitives.WriteInt16LittleEndian(value.AsSpan(2), QuantizeInt16((origin.Y + y) * 16f, "cube Y"));
            BinaryPrimitives.WriteInt16LittleEndian(value.AsSpan(4), QuantizeInt16((origin.Z + z) * 16f, "cube Z"));
            stream.Write(value);
        }
    }

    private static void WriteRecord(Span<byte> record, NativeGroup group, int dataOffsetRaw)
    {
        BinaryPrimitives.WriteSingleLittleEndian(record, group.Origin.X * 1024f);
        BinaryPrimitives.WriteSingleLittleEndian(record[4..], group.Origin.Y * 1024f);
        BinaryPrimitives.WriteSingleLittleEndian(record[8..], group.Origin.Z * 1024f);
        BinaryPrimitives.WriteSingleLittleEndian(record[12..], group.WorldRadius);
        BinaryPrimitives.WriteInt32LittleEndian(record[0x10..], dataOffsetRaw);
        WriteUInt16(record, 0x14, 0);
        WriteUInt16(record, 0x16, group.SharedOffset);
        WriteUInt16(record, 0x18, group.Lod1Offset);
        WriteUInt16(record, 0x1A, group.Lod01Offset);
        WriteUInt16(record, 0x1C, group.TextureOffset);
        WriteUInt16(record, 0x1E, group.RgbaOffset);
        record[0x20] = group.CommonSize;
        record[0x21] = group.Lod2Size;
        record[0x22] = group.Lod1Size;
        record[0x23] = group.Lod0Size;
        record[0x24] = group.RgbaCount;
        record[0x25] = group.RgbaCount;
        record[0x26] = group.RgbaCount;
        record[0x27] = 1;
        record[0x28] = group.TextureCount;
        record[0x29] = group.RgbaSize;
        record[0x2A] = group.RgbaAddress;
        record[0x2B] = byte.MaxValue;
        record[0x2C] = group.MeshSphereCount;
        record[0x2D] = group.HasPostScale ? (byte)1 : (byte)0;
        WriteUInt16(record, 0x2E, group.MeshSphereOffset);
        WriteUInt16(record, 0x30, group.LightOffset);
        WriteUInt16(record, 0x32, group.MeshSphereOffset);
        record[0x34] = byte.MaxValue;
        record[0x35] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(record[0x36..], ushort.MaxValue);
        WriteUInt16(record, 0x38, group.CubeOffset);
        BinaryPrimitives.WriteInt16LittleEndian(record[0x3A..], -1);
        record[0x3C] = group.VertexCount;
        record[0x3D] = group.TriangleCount;
        // Keep custom base-only terrain on the shared high-detail topology.
        BinaryPrimitives.WriteUInt16LittleEndian(record[0x3E..], ushort.MaxValue);
    }

    private static void WriteUnpack(
        Stream stream, int address, int count, byte command, bool signed, byte[] payload,
        Ps2VifCycle? cycle = null)
    {
        if (count is <= 0 or > byte.MaxValue || address is < 0 or > 0x3FF)
            throw new InvalidDataException("Generated tfrag VIF range exceeds native storage.");
        var immediate = checked((ushort)(0x8000 | (signed ? 0 : 0x4000) | address));
        WriteCommand(stream, immediate, checked((byte)count), command, payload, cycle);
    }

    private static void WriteCommand(
        Stream stream, ushort immediate, byte count, byte command, byte[] payload,
        Ps2VifCycle? cycle = null) => stream.Write(Ps2VifPacket.BuildBytes(immediate, count, command, payload, cycle));

    private static void WriteUInt16(Span<byte> bytes, int offset, int value)
    {
        if (value is < 0 or > ushort.MaxValue) throw new InvalidDataException("Generated tfrag offset exceeds native storage.");
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[offset..], checked((ushort)value));
    }

    private static short QuantizeUv(float value) =>
        QuantizeInt16(value * (value < 0 ? 8192f : 4096f), "texture coordinate");

    private static short QuantizeInt16(float value, string field)
    {
        var rounded = MathF.Round(value, MidpointRounding.ToEven);
        if (!float.IsFinite(rounded) || rounded is < short.MinValue or > short.MaxValue)
            throw new InvalidDataException($"Custom tfrag {field} exceeds signed 16-bit storage.");
        return (short)rounded;
    }

    private static int QuantizeInt32(float value, string field)
    {
        var rounded = MathF.Round(value, MidpointRounding.ToEven);
        if (!float.IsFinite(rounded) || rounded is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException($"Custom tfrag {field} exceeds signed 32-bit storage.");
        return (int)rounded;
    }

    private static byte QuantizeByte(float value, int scale)
    {
        var rounded = MathF.Round(value * scale, MidpointRounding.ToEven);
        if (!float.IsFinite(rounded) || rounded is < 0 or > byte.MaxValue)
            throw new InvalidDataException("Custom tfrag color or light value exceeds byte storage.");
        return (byte)rounded;
    }

    // LightTfrags expands this as RGBA5551 and replaces the runtime RGBA buffer.
    private static ushort EncodeColor5(TfragPieceVector4 value) => checked((ushort)(
        Quantize5(value.X) | Quantize5(value.Y) << 5 | Quantize5(value.Z) << 10 | 0x8000));

    private static int Quantize5(float value) =>
        Math.Clamp((int)MathF.Round(value * 31f, MidpointRounding.ToEven), 0, 31);

    private static ushort EncodeNormal(TfragPieceVector3 value)
    {
        var normal = Vector3.Normalize(new Vector3(value.X, value.Y, value.Z));
        if (!float.IsFinite(normal.X)) normal = Vector3.UnitZ;
        return checked((ushort)(AngleByte(MathF.Atan2(normal.Y, normal.X))
            | AngleByte(MathF.Asin(Math.Clamp(normal.Z, -1, 1))) << 8));
    }

    private static int AngleByte(float value)
    {
        var turns = value / (2f * MathF.PI);
        return ((int)MathF.Round(turns * 256f, MidpointRounding.ToEven) % 256 + 256) % 256;
    }

    private static void PadToFour(List<byte> values)
    {
        while ((values.Count & 3) != 0) values.Add(0);
    }

    private static int Offset(Stream stream) => checked((int)stream.Position);

    private static int AlignValue(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static void Align(Stream stream, int alignment)
    {
        while (stream.Position % alignment != 0) stream.WriteByte(0);
    }
}
