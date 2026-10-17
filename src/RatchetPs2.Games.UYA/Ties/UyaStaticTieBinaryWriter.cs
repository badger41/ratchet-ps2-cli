using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.UYA.Ties;

internal static partial class UyaStaticTieBinaryWriter
{
    public const int MaximumPackets = sbyte.MaxValue;
    internal const int RemapGroupHeaderSize = 0x20;
    // The runtime walks the group-size header until a zero entry, so one byte must remain as a terminator.
    internal const int MaximumRemapGroups = RemapGroupHeaderSize - 1;
    internal const int MaximumRemapSlotsPerGroup = 0x100;
    internal const int MaximumRemapGroupByteLength = 0x400;
    // LightTies stages the shared normal palette in the final 4 KiB of EE scratchpad.
    public const int MaximumVertexNormals = 0x1000 / NormalRecordSize;
    private const int LodCount = 3;
    private const int HeaderSize = 0x100;
    private const int PacketRecordSize = 0x10;
    private const int ShaderSize = 0x50;
    private const int NormalRecordSize = 8;
    private const float MaximumRetailCoordinate = 32_640f;
    private const byte DoubleSidedBfcDistance = 0;
    private const byte BackfaceCulledBfcDistance = 0x7f;

    public static UyaStaticTieBinaryResult Write(
        PreparedModel model,
        IReadOnlyList<BoundMaterial> materials,
        CancellationToken cancellationToken)
    {
        var scale = ComputeScale(model.Primitives);
        var packets = new List<PacketBuild>(model.PacketCount);
        var bindings = new List<StaticModelNativePrimitiveBinding>(model.Primitives.Count);
        var logicalVertices = new List<LogicalVertexBuild>(model.LogicalVertexCount);
        var firstTriangle = 0;
        foreach (var primitive in model.Primitives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = primitive.Source;
            var triangleCount = source.Indices.Length / 3;
            var faceNormals = BuildFaceNormals(source, cancellationToken);
            var firstPacket = packets.Count;
            foreach (var sourcePacket in primitive.Packets)
            {
                var packetVertices = new List<LogicalVertexBuild>(sourcePacket.Strips.Sum(strip => strip.Vertices.Count));
                foreach (var stripVertex in sourcePacket.Strips.SelectMany(strip => strip.Vertices))
                {
                    packetVertices.Add(BuildVertex(
                        source,
                        checked((int)stripVertex.SourceVertexIndex),
                        faceNormals[stripVertex.SourceTriangleIndex],
                        scale));
                }
                logicalVertices.AddRange(packetVertices);
                packets.Add(new(
                    primitive.MaterialIndex,
                    model.Materials[primitive.MaterialIndex].DoubleSided
                        ? DoubleSidedBfcDistance
                        : BackfaceCulledBfcDistance,
                    packetVertices,
                    sourcePacket.Strips.Select(strip => strip.Vertices.Count).ToArray()));
            }
            bindings.Add(new(
                source.SourceMeshIndex,
                source.SourcePrimitiveIndex,
                primitive.MaterialIndex,
                firstTriangle,
                triangleCount,
                firstPacket,
                packets.Count - firstPacket));
            firstTriangle += triangleCount;
        }

        foreach (var packet in packets)
            packet.Bytes = BuildPacketBytes(packet);
        var normalTable = BuildNormalTable(logicalVertices);
        var packetTableOffsets = new int[LodCount];
        var cursor = HeaderSize;
        packetTableOffsets[0] = cursor;
        cursor = Align16(checked(cursor + packets.Count * PacketRecordSize));
        var packetOffsets = new int[packets.Count];
        for (var packetIndex = 0; packetIndex < packets.Count; packetIndex++)
        {
            packetOffsets[packetIndex] = cursor;
            cursor = checked(cursor + packets[packetIndex].Bytes.Length);
        }
        var normalsOffset = Align16(cursor);
        var remapBytes = BuildRemapBytes(packets, normalTable.LogicalNormalIndices);
        // LightTies DMA-copies remap groups in qwords, including after an odd 8-byte normal count.
        var remapOffset = Align16(checked(normalsOffset + normalTable.Normals.Count * NormalRecordSize));
        cursor = remapOffset;
        cursor = checked(cursor + remapBytes.Length);
        var shadersOffset = Align16(cursor);
        var remapOffsets = new[] { remapOffset, shadersOffset, shadersOffset };
        var modelLength = Align16(checked(shadersOffset + materials.Count * ShaderSize));
        if (modelLength > StaticModelNativeLimits.MaximumModelBytes)
            throw new InvalidDataException(
                $"UYA TIE output would use {modelLength} bytes, over the {StaticModelNativeLimits.MaximumModelBytes}-byte archive-safe limit.");
        var bytes = new byte[modelLength];

        WriteHeader(bytes, model, materials.Count, packetTableOffsets, normalsOffset, remapOffsets, shadersOffset,
            packets, logicalVertices, normalTable.Normals.Count, scale);
        for (var packetIndex = 0; packetIndex < packets.Count; packetIndex++)
        {
            WritePacketRecord(
                bytes.AsSpan(packetTableOffsets[0] + packetIndex * PacketRecordSize),
                packets[packetIndex],
                packetTableOffsets[0],
                packetOffsets[packetIndex]);
            packets[packetIndex].Bytes.CopyTo(bytes, packetOffsets[packetIndex]);
        }
        for (var index = 0; index < normalTable.Normals.Count; index++)
            WriteNormal(bytes.AsSpan(normalsOffset + index * NormalRecordSize), normalTable.Normals[index]);
        remapBytes.CopyTo(bytes, remapOffset);
        foreach (var material in materials)
            WriteShader(bytes.AsSpan(shadersOffset + material.NativeIndex * ShaderSize), material);

        return new(
            bytes, bindings, logicalVertices, normalTable.LogicalNormalIndices,
            normalTable.Normals.Count, scale);
    }

    private static float ComputeScale(IReadOnlyList<PreparedPrimitive> primitives)
    {
        var maximum = 0f;
        foreach (var primitive in primitives)
        {
            var positions = primitive.Source.Positions;
            for (var index = 0; index < positions.Length; index += 3)
            {
                maximum = MathF.Max(maximum, MathF.Abs(positions[index]));
                maximum = MathF.Max(maximum, MathF.Abs(positions[index + 1]));
                maximum = MathF.Max(maximum, MathF.Abs(positions[index + 2]));
            }
        }
        return MathF.Max(1f / 1024f, maximum * 1024f / MaximumRetailCoordinate);
    }

    private static LogicalVertexBuild BuildVertex(
        StaticModelPrimitive source,
        int sourceVertex,
        Vector3 faceNormal,
        float scale)
    {
        var positionOffset = sourceVertex * 3;
        var ps2 = new Vector3(
            source.Positions[positionOffset],
            -source.Positions[positionOffset + 2],
            source.Positions[positionOffset + 1]);
        var unit = scale / 1024f;
        var x = Quantize(ps2.X, unit);
        var y = Quantize(ps2.Y, unit);
        var z = Quantize(ps2.Z, unit);
        var texCoordOffset = sourceVertex * 2;
        var u = source.TexCoords is null ? (ushort)0 : QuantizeTexCoord(source.TexCoords[texCoordOffset], source);
        var v = source.TexCoords is null ? (ushort)0 : QuantizeTexCoord(source.TexCoords[texCoordOffset + 1], source);
        var normal = source.Normals is null
            ? faceNormal
            : NormalizeOrFallback(new(
                source.Normals[positionOffset],
                source.Normals[positionOffset + 1],
                source.Normals[positionOffset + 2]), faceNormal);
        return new(x, y, z, u, v, normal);
    }

    private static Vector3[] BuildFaceNormals(
        StaticModelPrimitive source,
        CancellationToken cancellationToken)
    {
        var normals = new Vector3[source.Indices.Length / 3];
        for (var triangle = 0; triangle < normals.Length; triangle++)
        {
            if ((triangle & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = ReadPosition(source, source.Indices[triangle * 3]);
            var b = ReadPosition(source, source.Indices[triangle * 3 + 1]);
            var c = ReadPosition(source, source.Indices[triangle * 3 + 2]);
            normals[triangle] = NormalizeOrFallback(Vector3.Cross(b - a, c - a), Vector3.UnitY);
        }
        return normals;
    }

    private static Vector3 ReadPosition(StaticModelPrimitive source, uint index)
    {
        var offset = checked((int)index * 3);
        return new(source.Positions[offset], source.Positions[offset + 1], source.Positions[offset + 2]);
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback) =>
        value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : fallback;

    private static short Quantize(float value, float unit)
    {
        var quantized = MathF.Round(value / unit, MidpointRounding.AwayFromZero);
        if (quantized is < short.MinValue or > short.MaxValue)
            throw new InvalidDataException("A source position exceeds the computed UYA TIE quantization range.");
        return checked((short)quantized);
    }

    private static ushort QuantizeTexCoord(float value, StaticModelPrimitive source)
    {
        var quantized = MathF.Round(value * 4096f, MidpointRounding.AwayFromZero);
        if (quantized is < 0 or > ushort.MaxValue)
            throw new InvalidDataException(
                $"Source mesh {source.SourceMeshIndex}, primitive {source.SourcePrimitiveIndex} has a texture coordinate outside the UYA TIE fixed-point range.");
        return checked((ushort)quantized);
    }

    private static NormalTableBuild BuildNormalTable(IReadOnlyList<LogicalVertexBuild> vertices)
    {
        var exactNormals = new List<Vector3>();
        var exactIndices = new int[vertices.Count];
        var exactByRecord = new Dictionary<ulong, int>();
        Span<byte> record = stackalloc byte[NormalRecordSize];
        for (var index = 0; index < vertices.Count; index++)
        {
            record.Clear();
            WriteNormal(record, vertices[index].Normal);
            var key = BinaryPrimitives.ReadUInt64LittleEndian(record);
            if (!exactByRecord.TryGetValue(key, out var normalIndex))
            {
                normalIndex = exactNormals.Count;
                exactByRecord.Add(key, normalIndex);
                exactNormals.Add(vertices[index].Normal);
            }
            exactIndices[index] = normalIndex;
        }
        if (exactNormals.Count <= MaximumVertexNormals)
            return new(exactNormals, exactIndices);

        var uses = new int[exactNormals.Count];
        foreach (var index in exactIndices) uses[index]++;
        var selected = new int[MaximumVertexNormals];
        var selectedSources = new bool[exactNormals.Count];
        var nearestDistances = Enumerable.Repeat(float.PositiveInfinity, exactNormals.Count).ToArray();
        var nearestSelected = new int[exactNormals.Count];
        var unitNormals = exactNormals.Select(normal => NormalizeOrFallback(normal, Vector3.UnitY)).ToArray();
        for (var selectedIndex = 0; selectedIndex < selected.Length; selectedIndex++)
        {
            var sourceIndex = 0;
            if (selectedIndex == 0)
            {
                for (var index = 1; index < uses.Length; index++)
                    if (uses[index] > uses[sourceIndex]) sourceIndex = index;
            }
            else
            {
                for (var index = 1; index < exactNormals.Count; index++)
                {
                    if (selectedSources[index]) continue;
                    if (selectedSources[sourceIndex]
                        || nearestDistances[index] > nearestDistances[sourceIndex]
                        || (nearestDistances[index] == nearestDistances[sourceIndex]
                            && uses[index] > uses[sourceIndex]))
                        sourceIndex = index;
                }
            }
            selected[selectedIndex] = sourceIndex;
            selectedSources[sourceIndex] = true;
            var center = unitNormals[sourceIndex];
            for (var index = 0; index < exactNormals.Count; index++)
            {
                var distance = 1f - Math.Clamp(Vector3.Dot(center, unitNormals[index]), -1f, 1f);
                if (distance >= nearestDistances[index]) continue;
                nearestDistances[index] = distance;
                nearestSelected[index] = selectedIndex;
            }
        }
        return new(
            selected.Select(index => exactNormals[index]).ToArray(),
            exactIndices.Select(index => nearestSelected[index]).ToArray());
    }

    private static void WriteHeader(
        byte[] bytes,
        PreparedModel model,
        int materialCount,
        IReadOnlyList<int> packetTableOffsets,
        int normalsOffset,
        IReadOnlyList<int> remapOffsets,
        int shadersOffset,
        IReadOnlyList<PacketBuild> packets,
        IReadOnlyList<LogicalVertexBuild> vertices,
        int vertexNormalCount,
        float scale)
    {
        for (var index = 0; index < 3; index++)
        {
            WriteUInt32(bytes, index * 4, checked((uint)packetTableOffsets[index]));
            bytes[0x0c + index] = checked((byte)(index == 0 ? packets.Count : 0));
            WriteInt16(bytes, 0x24 + index * 2, checked((short)(index == 0
                ? packets.Sum(packet => (packet.VertexSize + 3) / 4)
                : 0)));
            WriteUInt16(bytes, 0x2a + index * 2, checked((ushort)(remapOffsets[index] - normalsOffset)));
        }
        bytes[0x0f] = checked((byte)materialCount);
        WriteSingle(bytes, 0x10, 12f);
        WriteSingle(bytes, 0x14, 25f);
        WriteSingle(bytes, 0x18, 8190f);
        WriteUInt32(bytes, 0x1c, checked((uint)shadersOffset));
        WriteUInt32(bytes, 0x34, checked((uint)normalsOffset));
        WriteInt16(bytes, 0x38, checked((short)vertexNormalCount));
        WriteInt16(bytes, 0x3a, checked((short)Align4(checked((vertexNormalCount + 2) * sizeof(ushort)))));
        WriteSingle(bytes, 0x40, scale);
        WriteInt16(bytes, 0x44, unchecked((short)model.TargetClassId));
        WriteSingle(bytes, 0x48, 10f);
        WriteInt32(bytes, 0x4c, 0);

        var center = vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + new Vector3(vertex.X, vertex.Y, vertex.Z)) / vertices.Count;
        var radius = vertices.Max(vertex => Vector3.Distance(center, new(vertex.X, vertex.Y, vertex.Z)));
        WriteSingle(bytes, 0x50, center.X / 1024f);
        WriteSingle(bytes, 0x54, center.Y / 1024f);
        WriteSingle(bytes, 0x58, center.Z / 1024f);
        WriteSingle(bytes, 0x5c, radius / 1024f);
        for (var index = 0; index < 3; index++)
        {
            WriteInt16(bytes, 0x60 + index * 8, checked((short)(index == 0 ? model.LogicalVertexCount : 0)));
            WriteInt16(bytes, 0x62 + index * 8, checked((short)(index == 0 ? model.NativeTriangleCount : 0)));
            WriteInt16(bytes, 0x64 + index * 8, checked((short)(index == 0 ? model.StripCount : 0)));
        }
        WriteBounds(bytes, vertices);
    }

    private static void WriteBounds(byte[] bytes, IReadOnlyList<LogicalVertexBuild> vertices)
    {
        var minX = vertices.Min(vertex => vertex.X);
        var minY = vertices.Min(vertex => vertex.Y);
        var minZ = vertices.Min(vertex => vertex.Z);
        var maxX = vertices.Max(vertex => vertex.X);
        var maxY = vertices.Max(vertex => vertex.Y);
        var maxZ = vertices.Max(vertex => vertex.Z);
        var corners = new (float X, float Y, float Z)[]
        {
            (maxX, maxY, minZ), (minX, maxY, minZ), (maxX, minY, minZ), (minX, minY, minZ),
            (maxX, maxY, maxZ), (minX, maxY, maxZ), (maxX, minY, maxZ), (minX, minY, maxZ),
        };
        for (var index = 0; index < corners.Length; index++)
        {
            var offset = 0x80 + index * 0x10;
            WriteSingle(bytes, offset, corners[index].X);
            WriteSingle(bytes, offset + 4, corners[index].Y);
            WriteSingle(bytes, offset + 8, corners[index].Z);
            WriteSingle(bytes, offset + 12, 1f);
        }
    }

    private static void WriteNormal(Span<byte> destination, Vector3 gltfNormal)
    {
        // LightTies stores the lighting-facing vector, opposite the outward surface normal.
        var normal = -NormalizeOrFallback(gltfNormal, Vector3.UnitY);
        var ps2 = new Vector3(normal.X, -normal.Z, normal.Y);
        destination[0] = unchecked((byte)checked((sbyte)Math.Clamp(MathF.Round(ps2.X * 127f), -127, 127)));
        destination[1] = unchecked((byte)checked((sbyte)Math.Clamp(MathF.Round(ps2.Y * 127f), -127, 127)));
        destination[2] = unchecked((byte)checked((sbyte)Math.Clamp(MathF.Round(ps2.Z * 127f), -127, 127)));
        destination[5] = 127;
        var azimuth = MathF.Atan2(normal.Z, -normal.X);
        var elevation = MathF.Asin(Math.Clamp(-normal.Y, -1f, 1f));
        var packedAzimuth = (int)MathF.Round(azimuth * 256f / (2f * MathF.PI)) & 0xff;
        var packedElevation = (int)MathF.Round(elevation * 256f / (2f * MathF.PI)) & 0xff;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], checked((ushort)(packedAzimuth | packedElevation << 8)));
    }

    private static Vector3 DecodePackedNormal(ushort packed)
    {
        var azimuth = (packed & 0xff) * (2f * MathF.PI / 256f);
        var elevation = (packed >> 8) * (2f * MathF.PI / 256f);
        var rawX = -MathF.Cos(azimuth) * MathF.Cos(elevation);
        var rawY = -MathF.Sin(azimuth) * MathF.Cos(elevation);
        var rawZ = -MathF.Sin(elevation);
        return Vector3.Normalize(new(rawX, rawZ, -rawY));
    }

    private static void WriteShader(Span<byte> destination, BoundMaterial material)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x08..], 6);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x10..], 0xFF82);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x14..], 4);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x18..], 0x14);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x28..], 0x34);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x30..], material.ClampU ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x34..], material.ClampV ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x38..], 8);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x40..], 0x44);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x44..], 0x80);
        BinaryPrimitives.WriteInt32LittleEndian(destination[0x48..], 0x42);
    }

    private static int Align16(int value) => checked((value + 0x0f) & ~0x0f);
    private static int Align4(int value) => checked((value + 3) & ~3);

    private static void WriteInt16(byte[] bytes, int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void WriteUInt16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void WriteInt32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void WriteUInt32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void WriteSingle(byte[] bytes, int offset, float value) => WriteInt32(bytes, offset, BitConverter.SingleToInt32Bits(value));
}

internal sealed record UyaStaticTieBinaryResult(
    byte[] ModelBytes,
    IReadOnlyList<StaticModelNativePrimitiveBinding> PrimitiveBindings,
    IReadOnlyList<LogicalVertexBuild> LogicalVertices,
    IReadOnlyList<int> LogicalNormalIndices,
    int VertexNormalCount,
    float Scale);

internal sealed record LogicalVertexBuild(short X, short Y, short Z, ushort S, ushort T, Vector3 Normal);

internal sealed record NormalTableBuild(
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<int> LogicalNormalIndices);

internal sealed record RemapAssignment(int NormalIndex, int TargetSlot);

internal sealed record RemapGroupBuild(
    int FirstSlot,
    int SlotCount,
    IReadOnlyList<RemapAssignment> Assignments,
    IReadOnlyList<int> ZeroTargetSlots,
    int ByteLength);

internal sealed class PacketBuild(
    int materialIndex,
    byte bfcDistance,
    IReadOnlyList<LogicalVertexBuild> vertices,
    IReadOnlyList<int> stripVertexCounts)
{
    public int MaterialIndex { get; } = materialIndex;
    public byte BfcDistance { get; } = bfcDistance;
    public IReadOnlyList<LogicalVertexBuild> Vertices { get; } = vertices;
    public IReadOnlyList<int> StripVertexCounts { get; } = stripVertexCounts;
    public byte[] Bytes { get; set; } = [];
    public byte ControlCount { get; set; }
    public byte ControlSize { get; set; }
    public byte VertexOffset { get; set; }
    public byte VertexSize { get; set; }
    public byte ScissorOffset { get; set; }
    public byte ScissorSize { get; set; }
}
