using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.IO.Vif;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.UYA.Builders;
using RatchetPs2.Games.UYA.Geometry;

namespace RatchetPs2.Games.UYA.Tfrags;

internal static partial class UyaStaticModelTfragWriter
{
    public const int GenerationVersion = 19;
    private const int TerrainHeaderSize = 0x40;
    private const int RecordSize = 0x40;

    public static byte[] Write(
        IReadOnlyList<StaticModelTfragPiece> pieces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        if (pieces.Count == 0) throw new InvalidDataException("A custom tfrag terrain needs at least one piece.");
        var nativePieces = pieces.SelectMany(piece => PrepareForNativeWrite(piece, cancellationToken)).ToArray();
        var groups = nativePieces.Select(piece =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BuildGroup(piece, cancellationToken);
        }).ToArray();

        using var stream = new MemoryStream();
        stream.SetLength(checked(TerrainHeaderSize + groups.Length * RecordSize));
        stream.Position = stream.Length;
        Align(stream, 0x10);
        for (var index = 0; index < groups.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataOffset = checked((int)stream.Position);
            stream.Write(groups[index].Data);
            Align(stream, 0x10);
            WriteRecord(stream.GetBuffer().AsSpan(TerrainHeaderSize + index * RecordSize, RecordSize),
                groups[index], dataOffset - TerrainHeaderSize);
        }

        var output = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(output, TerrainHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), groups.Length);
        BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(8), groups.Max(value => value.WorldRadius) / 1024f);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12), groups.Length);
        Validate(output, nativePieces, cancellationToken);
        return output;
    }

    public static int CountNativeGroups(
        StaticModelTfragPiece piece,
        CancellationToken cancellationToken = default) =>
        PrepareForNativeWrite(piece, cancellationToken).Count;

    private static NativeGroup BuildGroup(
        StaticModelTfragPiece piece,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(piece.Asset);
        var geometry = piece.Asset.Geometry;
        if (geometry.Lods.Count != 3 || geometry.Lods[0].Primitives.Count == 0
            || geometry.Lods[1].Primitives.Count != 0 || geometry.Lods[2].Primitives.Count != 0)
            throw new InvalidDataException($"Custom tfrag partition {piece.PartitionIndex} must contain only LOD0 geometry.");
        if (piece.Transform.Rotation != new TfragPieceVector4(0, 0, 0, 1)
            || piece.Transform.Scale != new TfragPieceVector3(1, 1, 1))
            throw new InvalidDataException($"Custom tfrag partition {piece.PartitionIndex} must use its import transform.");

        var primitives = geometry.Lods[0].Primitives;
        var sourceVertexCount = primitives.Sum(value => value.Vertices.Count);
        var triangleCount = primitives.Sum(value => value.Indices.Count / 3);
        if (sourceVertexCount is <= 0 or > byte.MaxValue || triangleCount <= 0)
            throw new InvalidDataException($"Custom tfrag partition {piece.PartitionIndex} exceeds native count storage.");
        if (triangleCount > UyaStaticModelTfragPartitioner.MaximumTrianglesPerPartition)
            throw new InvalidDataException(
                $"Custom tfrag partition {piece.PartitionIndex} has {triangleCount} strip rows; "
                + $"the safe UYA limit is {UyaStaticModelTfragPartitioner.MaximumTrianglesPerPartition}. "
                + "Re-import the custom tfrag to repartition it.");
        var materials = primitives.Select(value => value.Material).Distinct().ToArray();
        if (materials.Any(value => value.TextureId is < 0 or > byte.MaxValue))
            throw new InvalidDataException(
                $"Custom tfrag partition {piece.PartitionIndex} contains a texture ID outside native byte storage.");
        if (materials.Length > UyaStaticModelTfragPartitioner.MaximumMaterialsPerPartition)
            throw new InvalidDataException($"Custom tfrag partition {piece.PartitionIndex} has too many materials.");
        var streamingTextureIds = piece.StreamingTextureIds.Count > 0
            ? piece.StreamingTextureIds
            : geometry.TextureIds;
        if (streamingTextureIds.Count > byte.MaxValue
            || streamingTextureIds.Any(value => value is < 0 or > byte.MaxValue)
            || streamingTextureIds.Distinct().Count() != streamingTextureIds.Count)
            throw new InvalidDataException(
                $"Custom tfrag partition {piece.PartitionIndex} has an invalid streaming texture inventory.");

        var materialSlots = materials.Select((value, index) => (value, index)).ToDictionary(value => value.value, value => value.index);
        var packetPlan = UyaTfragPacketPlanner.Build(primitives.Select(primitive =>
            new UyaTfragPacketPrimitive(
                materialSlots[primitive.Material],
                primitive.Vertices.Count,
                primitive.Indices.ToArray())).ToArray(), cancellationToken);
        var vertices = new List<TfragPieceVertex>(checked(triangleCount * 4));
        var stripRows = new List<StripRow>();
        var stripIndices = new List<byte>();
        var meshSpheres = streamingTextureIds.Select(textureId => new MeshSphere(
            Vector3.Zero,
            geometry.Bounds.Radius,
            checked((byte)textureId))).ToArray();
        var activeMaterialSlot = -1;
        var packetVertexCount = 0;
        var encodedTriangleCount = 0;
        for (var primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primitive = primitives[primitiveIndex];
            ValidatePrimitive(primitive, piece.PartitionIndex);
            BuildStrips(
                primitive,
                packetPlan.StripsByPrimitive[primitiveIndex],
                materialSlots[primitive.Material],
                vertices,
                stripRows,
                stripIndices,
                cancellationToken,
                ref activeMaterialSlot,
                ref packetVertexCount,
                ref encodedTriangleCount);
        }
        EndGsPacket(stripRows);
        while ((vertices.Count & 3) != 0) vertices.Add(vertices[^1]);
        var vertexCount = vertices.Count;
        if (vertexCount != packetPlan.VertexCount
            || stripRows.Count + 1 != packetPlan.StripRowCount
            || stripIndices.Count != packetPlan.StripIndexCount)
            throw new InvalidDataException(
                $"Custom tfrag partition {piece.PartitionIndex} packet plan changed during native encoding.");
        if (vertexCount > UyaStaticModelTfragPartitioner.MaximumVerticesPerPartition)
            throw new InvalidDataException(
                $"Custom tfrag partition {piece.PartitionIndex} needs {vertexCount} native face vertices; "
                + "re-import the custom tfrag to repartition it.");
        if (encodedTriangleCount > byte.MaxValue)
            throw new InvalidDataException($"Custom tfrag partition {piece.PartitionIndex} exceeds native paired-triangle storage.");
        stripRows.Add(new(0, -1, -1, -1));
        PadToFour(stripIndices);

        var textureAddress = 9;
        var positionAddress = checked(textureAddress + materials.Length * 5);
        var vertexInfoAddress = checked(positionAddress + vertexCount * 2);
        var indicesAddress = checked(vertexInfoAddress + vertexCount);
        var stripsAddress = checked(indicesAddress + stripIndices.Count / 4);
        var endAddress = checked(stripsAddress + stripRows.Count);
        if (endAddress != packetPlan.EndAddress)
            throw new InvalidDataException(
                $"Custom tfrag partition {piece.PartitionIndex} VU address plan changed during native encoding.");
        if (!packetPlan.FitsVuBuffer)
            throw new InvalidDataException(
                $"Custom tfrag partition {piece.PartitionIndex} needs {endAddress} VU qwords "
                + $"({vertexCount} vertices, {stripIndices.Count} strip indices, {stripRows.Count} strip rows); "
                + $"the limit is {UyaTfragPacketPlanner.VuBufferQwords}.");
        if (positionAddress + 1 > byte.MaxValue)
            throw new InvalidDataException($"Custom tfrag partition {piece.PartitionIndex} RGBA address exceeds native storage.");

        var basePosition = new Vector4(
            QuantizeInt32(piece.Transform.Position.X * 1024f, "origin X"),
            QuantizeInt32(piece.Transform.Position.Y * 1024f, "origin Y"),
            QuantizeInt32(piece.Transform.Position.Z * 1024f, "origin Z"),
            0);
        using var data = new MemoryStream();
        WriteLod2(data, stripIndices, stripRows, vertexInfoAddress, indicesAddress, stripsAddress);
        Align(data, 0x10);
        var sharedOffset = Offset(data);
        var textureOffset = WriteCommon(data, materials, vertices, basePosition,
            positionAddress, vertexInfoAddress, indicesAddress, stripsAddress);
        Align(data, 0x10);
        var lod1Offset = Offset(data);
        WriteLod1(data, stripIndices, stripRows, vertexInfoAddress, indicesAddress, stripsAddress);
        Align(data, 0x10);
        var lod01Offset = Offset(data);
        WriteCommand(data, 0, 0, 0x30, VectorPayload(basePosition));
        WriteCommand(data, new Ps2VifCycle(2, 1).Immediate, 0, 0x01, []);
        Align(data, 0x10);
        var lod0TopologyOffset = Offset(data);
        WriteCommand(data, 0, 0, 0x05, []);
        WriteCommand(data, new Ps2VifCycle(4, 4).Immediate, 0, 0x01, []);
        WriteUnpack(data, stripsAddress, stripRows.Count, 0x6E, signed: true, StripPayload(stripRows));
        WriteIndices(data, stripIndices, vertexInfoAddress, indicesAddress);
        WriteCommand(data, 0, 0, 0x05, []);
        Align(data, 0x10);
        var vifEnd = Offset(data);

        var rgbaOffset = Offset(data);
        foreach (var vertex in vertices) WriteColor(data, vertex.Color);
        while ((vertices.Count & 3) != 0 && data.Position < rgbaOffset + AlignValue(vertices.Count, 4) * 4)
            data.Write(new byte[4]);
        Align(data, 0x10);
        var lightOffset = Offset(data);
        data.Write(VectorPayload(basePosition));
        foreach (var vertex in vertices) WriteLight(data, vertex);
        Align(data, 0x10);
        var meshSphereOffset = Offset(data);
        var meshSphereCount = meshSpheres.Length;
        WriteMeshSpheres(data, piece.Transform.Position, meshSpheres);
        Align(data, 0x10);
        var cubeOffset = Offset(data);
        WriteCube(data, piece.Transform.Position, geometry.Bounds);
        Align(data, 0x10);

        var rgbaCount = checked((byte)AlignValue(vertexCount, 4));
        return new(
            data.ToArray(),
            piece.Transform.Position,
            geometry.Bounds.Radius * 1024f,
            sharedOffset,
            lod1Offset,
            lod01Offset,
            textureOffset,
            rgbaOffset,
            checked((byte)((lod1Offset - sharedOffset) / 16)),
            checked((byte)(lod1Offset / 16)),
            checked((byte)((lod0TopologyOffset - sharedOffset) / 16)),
            checked((byte)((vifEnd - lod01Offset) / 16)),
            rgbaCount,
            checked((byte)materials.Length),
            checked((byte)(AlignValue(vertexCount, 4) / 4)),
            checked((byte)(positionAddress + 1)),
            lightOffset,
            meshSphereOffset,
            checked((byte)meshSphereCount),
            cubeOffset,
            checked((byte)vertexCount),
            checked((byte)encodedTriangleCount),
            vertices.Any(value => MathF.Abs(value.LightPostScale - 1) > 0.00001f));
    }

    private static void WriteLod2(
        Stream stream,
        IReadOnlyList<byte> indices,
        IReadOnlyList<StripRow> strips,
        int vertexInfoAddress,
        int indicesAddress,
        int stripsAddress)
    {
        WriteIndices(stream, indices, vertexInfoAddress, indicesAddress);
        WriteCommand(stream, 0, 0, 0x05, []);
        WriteUnpack(stream, stripsAddress, strips.Count, 0x6E, signed: true, StripPayload(strips));
    }

    private static void WriteLod1(
        Stream stream,
        IReadOnlyList<byte> indices,
        IReadOnlyList<StripRow> strips,
        int vertexInfoAddress,
        int indicesAddress,
        int stripsAddress)
    {
        WriteUnpack(stream, stripsAddress, strips.Count, 0x6E, signed: true, StripPayload(strips));
        WriteIndices(stream, indices, vertexInfoAddress, indicesAddress);
    }

    private static void WriteIndices(Stream stream, IReadOnlyList<byte> indices, int vertexInfoAddress, int indicesAddress)
    {
        WriteCommand(stream, 0, 0, 0x30, IntVectorPayload(vertexInfoAddress));
        WriteCommand(stream, 1, 0, 0x05, []);
        WriteUnpack(stream, indicesAddress, indices.Count / 4, 0x6E, signed: false, indices.ToArray());
    }

    private static int WriteCommon(
        Stream stream,
        IReadOnlyList<TfragPieceMaterial> materials,
        IReadOnlyList<TfragPieceVertex> vertices,
        Vector4 basePosition,
        int positionAddress,
        int vertexInfoAddress,
        int indicesAddress,
        int stripsAddress)
    {
        var header = new byte[40];
        WriteUInt16(header, 0x00, vertices.Count);
        WriteUInt16(header, 0x0C, positionAddress);
        WriteUInt16(header, 0x0E, vertexInfoAddress);
        // The renderer consumes the complete address map even when the two
        // finer LODs contribute no extra vertices. Retail base-only tfrags
        // mirror each empty address/padding pair to the next allocated region;
        // leaving the undocumented half of a pair at zero makes the VU program
        // dereference invalid scratchpad state.
        WriteUInt16(header, 0x10, indicesAddress);
        WriteUInt16(header, 0x12, indicesAddress);
        WriteUInt16(header, 0x14, indicesAddress);
        WriteUInt16(header, 0x16, indicesAddress);
        WriteUInt16(header, 0x18, indicesAddress);
        WriteUInt16(header, 0x1A, indicesAddress);
        WriteUInt16(header, 0x1C, indicesAddress);
        WriteUInt16(header, 0x1E, indicesAddress);
        WriteUInt16(header, 0x20, indicesAddress);
        WriteUInt16(header, 0x22, indicesAddress);
        WriteUInt16(header, 0x24, stripsAddress);
        WriteUInt16(header, 0x26, 9);
        WriteUnpack(stream, 0, 5, 0x6D, signed: false, header);
        var textureOffset = checked(Offset(stream) + 4);
        WriteUnpack(stream, 9, materials.Count * 5, 0x6C, signed: true, TexturePayload(materials));
        WriteCommand(stream, 0, 0, 0x30, SingleVertexInfoRow(positionAddress));
        WriteCommand(stream, 1, 0, 0x05, []);
        WriteUnpack(stream, vertexInfoAddress, vertices.Count, 0x6D, signed: true, VertexInfoPayload(vertices));
        WriteCommand(stream, 0, 0, 0x30, VectorPayload(basePosition));
        var positionCycle = new Ps2VifCycle(2, 1);
        WriteCommand(stream, positionCycle.Immediate, 0, 0x01, []);
        WriteUnpack(stream, positionAddress, vertices.Count, 0x69, signed: true,
            PositionPayload(vertices), positionCycle);
        WriteCommand(stream, new Ps2VifCycle(4, 4).Immediate, 0, 0x01, []);
        WriteCommand(stream, 0, 0, 0x05, []);
        return textureOffset;
    }

    private static void BuildStrips(
        TfragPiecePrimitive primitive,
        IReadOnlyList<UyaTriangleStrip> strips,
        int materialSlot,
        List<TfragPieceVertex> vertices,
        List<StripRow> rows,
        List<byte> indices,
        CancellationToken cancellationToken,
        ref int activeMaterialSlot,
        ref int packetVertexCount,
        ref int encodedTriangleCount)
    {
        var vertexBase = vertices.Count;
        vertices.AddRange(primitive.Vertices);
        foreach (var strip in strips)
        {
            var stripIndices = strip.Vertices.Select(vertex =>
                checked(vertexBase + (int)vertex.SourceVertexIndex)).ToArray();
            AddStrip(
                stripIndices,
                materialSlot,
                rows,
                indices,
                ref activeMaterialSlot,
                ref packetVertexCount);
            encodedTriangleCount = checked(encodedTriangleCount + stripIndices.Length - 2);
        }
    }

    private static void AddStrip(
        IReadOnlyList<int> stripIndices,
        int materialSlot,
        List<StripRow> rows,
        List<byte> indices,
        ref int activeMaterialSlot,
        ref int packetVertexCount)
    {
        if (stripIndices.Count is < 4 or > UyaTfragPacketPlanner.MaximumVerticesPerGsPacket
            || (stripIndices.Count & 1) != 0)
            throw new InvalidDataException("Custom tfrag strip has an invalid native vertex count.");
        if (packetVertexCount > 0
            && packetVertexCount + stripIndices.Count > UyaTfragPacketPlanner.MaximumVerticesPerGsPacket)
        {
            EndGsPacket(rows);
            packetVertexCount = 0;
            activeMaterialSlot = -1;
        }
        var materialChanged = activeMaterialSlot != materialSlot;
        rows.Add(materialChanged
            ? new(checked((sbyte)(stripIndices.Count - 128)), 0, checked((sbyte)(materialSlot * 5)), 0)
            : new(checked((sbyte)stripIndices.Count), 0, 0, 0));
        indices.AddRange(stripIndices.Select(checkedValue => checked((byte)checkedValue)));
        packetVertexCount += stripIndices.Count;
        activeMaterialSlot = materialSlot;
    }

    private static void EndGsPacket(List<StripRow> rows)
    {
        if (rows.Count == 0 || rows[^1].Count == 0) return;
        var last = rows[^1];
        rows[^1] = last with
        {
            Count = last.Count > 0 ? checked((sbyte)(last.Count - 128)) : last.Count,
            Control = sbyte.MinValue,
            TextureOffset = last.Count > 0 ? (sbyte)-1 : last.TextureOffset,
        };
    }

    private static void ValidatePrimitive(TfragPiecePrimitive primitive, int partitionIndex)
    {
        if (primitive.Vertices.Count == 0 || primitive.Indices.Count == 0 || primitive.Indices.Count % 3 != 0
            || primitive.Indices.Any(value => value >= primitive.Vertices.Count))
            throw new InvalidDataException($"Custom tfrag partition {partitionIndex} has invalid primitive topology.");
    }

    private static void Validate(byte[] bytes, IReadOnlyList<StaticModelTfragPiece> pieces, CancellationToken cancellationToken)
    {
        var terrain = TfragTerrainReader.Read(bytes);
        if (terrain.Chunks.Count != pieces.Count) throw new InvalidDataException("Generated tfrag group inventory did not re-read.");
        for (var index = 0; index < pieces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateBaseOnlyAddressMap(bytes, terrain.Chunks[index], pieces[index].PartitionIndex);
            var streamingTextureIds = pieces[index].StreamingTextureIds.Count > 0
                ? pieces[index].StreamingTextureIds
                : pieces[index].Asset.Geometry.TextureIds;
            if (terrain.Chunks[index].MSphereCount != streamingTextureIds.Count)
                throw new InvalidDataException(
                    $"Generated tfrag partition {pieces[index].PartitionIndex} lost its streaming texture inventory.");
            var actual = TfragGltfExporter.DecodeCanonicalPiece(terrain, terrain.Chunks[index], cancellationToken);
            var expected = MergeByMaterial(pieces[index].Asset.Geometry.Lods[0].Primitives);
            foreach (var lod in actual.Lods)
            {
                var merged = MergeByMaterial(lod.Primitives);
                // A lone face is encoded as one padded four-index VU row. The retail-compatible
                // row re-reads structurally, but the heuristic GLTF decoder intentionally drops it.
                if (expected.Sum(value => value.Value.Indices.Count / 3) == 1
                    && merged.Count == 0
                    && terrain.Chunks[index].VertexCount >= 3
                    && terrain.Chunks[index].TriangleCount == 2)
                    continue;
                if (merged.Count != expected.Count || expected.Any(pair =>
                        !merged.TryGetValue(pair.Key, out var primitive)
                        || !UyaTfragTerrainComposer.TrianglesEquivalentIgnoringWinding(pair.Value, primitive)))
                    throw new InvalidDataException(
                        $"Generated tfrag partition {pieces[index].PartitionIndex} failed semantic re-read at LOD {lod.LodIndex} "
                        + $"({expected.Sum(value => value.Value.Indices.Count / 3)} expected triangles in {expected.Count} materials; "
                        + $"{merged.Sum(value => value.Value.Indices.Count / 3)} actual triangles in {merged.Count} materials; "
                        + $"first expected {DescribeFirstOrEmpty(expected.Values)}; "
                        + $"first actual {DescribeFirstOrEmpty(merged.Values)}).");
            }
        }
    }

    private static void ValidateBaseOnlyAddressMap(byte[] bytes, TfragChunk chunk, int partitionIndex)
    {
        var header = bytes.AsSpan(chunk.DataOffset + chunk.SharedOffset + 4, 0x28);
        var indicesAddress = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1A..]);
        ReadOnlySpan<int> mirroredOffsets = [0x10, 0x12, 0x14, 0x16, 0x18, 0x1C, 0x1E, 0x20, 0x22];
        foreach (var offset in mirroredOffsets)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(header[offset..]) != indicesAddress)
                throw new InvalidDataException(
                    $"Generated tfrag partition {partitionIndex} has an incomplete base-only VU address map.");
        }
    }

    private static string DescribeFirstOrEmpty(IEnumerable<TfragPiecePrimitive> primitives)
    {
        using var enumerator = primitives.GetEnumerator();
        return enumerator.MoveNext() ? DescribeFirst(enumerator.Current) : "none";
    }

    private static Dictionary<TfragPieceMaterial, TfragPiecePrimitive> MergeByMaterial(
        IReadOnlyList<TfragPiecePrimitive> primitives) => primitives.GroupBy(value => value.Material)
        .ToDictionary(group => group.Key, group =>
        {
            var vertices = new List<TfragPieceVertex>();
            var indices = new List<uint>();
            foreach (var primitive in group)
            {
                var offset = checked((uint)vertices.Count);
                vertices.AddRange(primitive.Vertices);
                indices.AddRange(primitive.Indices.Select(value => checked(value + offset)));
            }
            return new TfragPiecePrimitive(group.Key, vertices, indices);
        });

    private static string DescribeFirst(TfragPiecePrimitive primitive)
    {
        var a = primitive.Vertices[checked((int)primitive.Indices[0])];
        var b = primitive.Vertices[checked((int)primitive.Indices[1])];
        var c = primitive.Vertices[checked((int)primitive.Indices[2])];
        return $"p0={a.Position},p1={b.Position},p2={c.Position},uv={a.TexCoord},c={a.Color},ln={a.LightNormal}";
    }

    private readonly record struct StripRow(sbyte Count, sbyte Control, sbyte TextureOffset, sbyte Padding);

    private readonly record struct MeshSphere(Vector3 Center, float Radius, byte TextureId);

    private sealed record NativeGroup(
        byte[] Data,
        TfragPieceVector3 Origin,
        float WorldRadius,
        int SharedOffset,
        int Lod1Offset,
        int Lod01Offset,
        int TextureOffset,
        int RgbaOffset,
        byte CommonSize,
        byte Lod2Size,
        byte Lod1Size,
        byte Lod0Size,
        byte RgbaCount,
        byte TextureCount,
        byte RgbaSize,
        byte RgbaAddress,
        int LightOffset,
        int MeshSphereOffset,
        byte MeshSphereCount,
        int CubeOffset,
        byte VertexCount,
        byte TriangleCount,
        bool HasPostScale);
}
