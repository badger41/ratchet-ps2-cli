using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Ties;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Games.RC1.Ties;
using RatchetPs2.Games.UYA.Ties;

namespace RatchetPs2.Games.RC1.Level;

internal static class Rc1TieModelConverter
{
    public static byte[] Convert(byte[] bytes, int classId, CancellationToken token,
        IReadOnlyList<FrontendAssetTexture>? textures = null, Analysis? analysis = null)
    {
        analysis ??= new(bytes);
        var tie = analysis.Tie;
        var doubleSided = new HashSet<int>();
        if (textures is not null)
        {
            if (textures.Count != tie.Header.TextureCount)
                throw new InvalidDataException("RC1 tie texture count does not match its material slots.");
            for (var index = 0; index < textures.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                if (textures[index] is null || textures[index].Role != 0)
                    throw new InvalidDataException("RC1 tie material texture is invalid.");
                // Test referenced pixels, not unused transparent entries in the CLUT.
                if (TextureConverter.AnalyzeAlpha(TextureConverter.Decode(PifReader.Read(textures[index].PifBytes))).HasAlpha)
                    doubleSided.Add(index);
            }
        }
        var packets = new List<UyaTieRebuiltPacket>[3];
        var sourceNormals = new Dictionary<int, int>[3];
        for (var lod = 0; lod < 3; lod++)
        {
            token.ThrowIfCancellationRequested();
            packets[lod] = [];
            sourceNormals[lod] = [];
            var topology = tie.LodTopologies[lod];
            if (topology.Triangles.Count == 0) continue;
            var facing = analysis.Facing(lod);
            var colorBase = 0;
            foreach (var packet in tie.PacketTables[lod].Packets)
            {
                token.ThrowIfCancellationRequested();
                var block = analysis.Blocks[(lod, packet.PacketIndex)];
                foreach (var vertex in block.DecodedVertices)
                {
                    if (!analysis.Normals.TryGetValue((lod, packet.PacketIndex, vertex.SourceRowIndex), out var normal))
                        throw new InvalidDataException($"RC1 tie LOD {lod}, packet {packet.PacketIndex} has an unresolved lighting index.");
                    sourceNormals[lod].Add(colorBase + vertex.Index, normal);
                }
                // RC1 uses the later multipass fields for its inline lighting-index table.
                var runs = analysis.Strips[lod][packet.PacketIndex]
                    .SelectMany(strip => Split(topology, strip, block.StripControls[strip.PacketStripIndex], facing)).ToArray();
                // Culling is packet-wide in UYA; split mixed materials without reordering strips.
                for (var start = 0; start < runs.Length;)
                {
                    var alpha = doubleSided.Contains(runs[start].Shader);
                    var end = start + 1;
                    while (end < runs.Length && (packet.BfcDistance == 0 || doubleSided.Contains(runs[end].Shader) == alpha)) end++;
                    var target = new TiePacket { LodIndex = lod, PacketIndex = packet.PacketIndex,
                        AbsoluteDataOffset = packet.AbsoluteDataOffset, BfcDistance = alpha ? (byte)0 : packet.BfcDistance };
                    packets[lod].AddRange(UyaTiePacketWriter.Build(bytes, target, block, colorBase, runs[start..end]));
                    start = end;
                }
                colorBase += Align4(packet.RgbaCount);
            }
            if (packets[lod].Count > byte.MaxValue)
                throw new InvalidDataException($"RC1 tie LOD {lod} exceeds UYA's 255-packet limit.");
        }
        using var output = new MemoryStream();
        output.Write(new byte[0x100]);
        var tables = new int[3];
        for (var lod = 0; lod < 3; lod++)
        {
            tables[lod] = checked((int)output.Position);
            output.Write(new byte[packets[lod].Count * 16]);
        }
        for (var lod = 0; lod < 3; lod++)
            foreach (var packet in packets[lod])
            {
                BinaryPrimitives.WriteInt32LittleEndian(packet.Record, checked((int)output.Position - tables[lod]));
                output.Write(packet.Bytes);
            }
        var normalOffset = checked((int)output.Position);
        foreach (var normal in tie.VertexNormals) output.Write(EncodeNormal(normal));
        Align(output);
        var remaps = new int[3];
        for (var lod = 0; lod < 3; lod++)
        {
            token.ThrowIfCancellationRequested();
            if (packets[lod].Count == 0) continue;
            remaps[lod] = checked((int)output.Position - normalOffset);
            output.Write(BuildRemaps(packets[lod], sourceNormals[lod]));
        }
        var shaderOffset = checked((int)output.Position);
        output.Write(bytes.AsSpan(checked((int)tie.Header.ShadersOffset)));
        var result = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x1c), shaderOffset);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x34), normalOffset);
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x38), checked((short)tie.VertexNormals.Count));
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x3a), checked((short)Align4((tie.VertexNormals.Count + 2) * 2)));
        BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(0x40), tie.Header.Scale);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x44), checked((ushort)classId));
        bytes.AsSpan(0x48, 8).CopyTo(result.AsSpan(0x48));
        bytes.AsSpan(0x10, 12).CopyTo(result.AsSpan(0x10));
        bytes.AsSpan(0x30, 16).CopyTo(result.AsSpan(0x50));
        result[15] = tie.Header.TextureCount;
        for (var lod = 0; lod < 3; lod++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(lod * 4), tables[lod]);
            result[12 + lod] = checked((byte)packets[lod].Count);
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x24 + lod * 2),
                checked((short)(packets[lod].Sum(packet => packet.SourceColorSlots.Length) / 4)));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x2a + lod * 2), checked((ushort)remaps[lod]));
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x60 + lod * 8), checked((short)packets[lod].Sum(packet => packet.LogicalVertices)));
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x62 + lod * 8), checked((short)packets[lod].Sum(packet => packet.Triangles)));
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x64 + lod * 8), checked((short)packets[lod].Sum(packet => packet.Strips)));
            for (var index = 0; index < packets[lod].Count; index++) packets[lod][index].Record.CopyTo(result, tables[lod] + index * 16);
        }
        WriteBounds(result, tie);
        token.ThrowIfCancellationRequested();
        _ = TieClassReader.Read(result, TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(RatchetPs2.Core.Games.GameId.UYA)));
        return result;
    }

    internal sealed class Analysis
    {
        private readonly IReadOnlyList<TieTriangle>?[] _facing = new IReadOnlyList<TieTriangle>?[3];
        public TieClass Tie { get; }
        public IReadOnlyDictionary<(int Lod, int Packet), TiePacketDataBlock> Blocks { get; }
        public IReadOnlyDictionary<(int Lod, int Packet, int Row), int> Normals { get; }
        public ILookup<int, TieTriangleStrip>[] Strips { get; }

        public Analysis(byte[] bytes)
        {
            Tie = TieClassReader.Read(bytes, TieClassReadOptions.ForGameProfile(Rc1TieGameProfile.Default));
            if (Tie.VertexNormals.Count is 0 or > 512)
                throw new InvalidDataException("RC1 tie lighting palette exceeds UYA scratchpad limits.");
            Blocks = Tie.PacketDataBlocks.ToDictionary(block => (block.LodIndex, block.PacketIndex));
            Normals = Tie.VertexNormalRemaps.ToDictionary(remap =>
                (remap.LodIndex, remap.PacketIndex, remap.VertexRowIndex), remap => remap.NormalIndex);
            Strips = Tie.LodTopologies.Select(topology => topology.Strips.ToLookup(strip => strip.PacketIndex)).ToArray();
        }

        public IReadOnlyList<TieTriangle> Facing(int lod) => _facing[lod] ??=
            TieGltfExporter.ResolveTriangleFacing(Tie, lod, Rc1TieGameProfile.Default);
    }

    private static IEnumerable<UyaTieStripRun> Split(TieLodTopology topology, TieTriangleStrip strip,
        TiePacketStripControl control, IReadOnlyList<TieTriangle> facing)
    {
        var phases = new bool?[strip.TriangleCount];
        for (var index = 0; index < phases.Length; index++)
        {
            var triangle = topology.Triangles[strip.TriangleStartIndex + index];
            Vector3 Position(int vertex) { var v = topology.LogicalVertices[vertex].DecodedVertex!; return new(v.X, v.Y, v.Z); }
            if (Vector3.Cross(Position(triangle.B) - Position(triangle.A), Position(triangle.C) - Position(triangle.A)).LengthSquared() != 0)
                phases[index] = facing[strip.TriangleStartIndex + index].B == triangle.C;
        }
        var previous = phases.FirstOrDefault(value => value is not null) ?? false;
        for (var index = 0; index < phases.Length; index++) previous = (phases[index] ??= previous);
        var start = 0;
        while (start < strip.TriangleCount)
        {
            var phase = phases[start]!.Value;
            var end = start + 1;
            while (end < strip.TriangleCount && end - start < 62
                && phases[end] == phase) end++;
            var vertices = strip.LogicalVertices.Skip(start).Take(end - start + 2).ToList();
            if (vertices.Count % 2 != 0) vertices.Add(vertices[^1]);
            var reverse = ((strip.Flags & 0x20) != 0) ^ phase ^ ((start & 1) != 0);
            yield return new(vertices.ToArray(), strip.ShaderIndex ?? throw new InvalidDataException("RC1 strip has no shader."),
                (byte)((strip.Flags & ~0x20) | (reverse ? 0x20 : 0)), control.ControlData1);
            start = end;
        }
    }

    private static byte[] BuildRemaps(IReadOnlyList<UyaTieRebuiltPacket> packets, IReadOnlyDictionary<int, int> normals)
    {
        var groups = new List<byte[]>();
        var slots = new List<int>();
        foreach (var packet in packets)
        {
            if (slots.Count > 0 && (slots.Count + packet.SourceColorSlots.Length > 256
                || Size(slots.Concat(packet.SourceColorSlots)) > 0x400)) Flush();
            slots.AddRange(packet.SourceColorSlots);
            if (slots.Count > 256 || Size(slots) > 0x400) throw new InvalidDataException("RC1 tie exceeds UYA's lighting scratch buffer.");
        }
        if (slots.Count > 0) Flush();
        if (groups.Count > 31) throw new InvalidDataException($"RC1 tie needs {groups.Count} lighting groups for {packets.Sum(packet => packet.SourceColorSlots.Length)} cache slots, exceeding UYA's limit of 31.");
        var result = new byte[32 + groups.Sum(group => group.Length)];
        var cursor = 32;
        for (var index = 0; index < groups.Count; index++)
        {
            result[index] = checked((byte)(groups[index].Length / 16));
            groups[index].CopyTo(result, cursor); cursor += groups[index].Length;
        }
        return result;

        static int Size(IEnumerable<int> values) => Align16(16 + values.Sum(slot => slot < 0 ? 2 : 4));
        void Flush()
        {
            var group = new byte[Size(slots)];
            var cursor = 16;
            for (var index = 0; index < slots.Count; index++)
                if (slots[index] >= 0)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(cursor), checked((ushort)(normals[slots[index]] * 4)));
                    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(cursor + 2), checked((ushort)(index * 4)));
                    cursor += 4;
                }
            BinaryPrimitives.WriteUInt16LittleEndian(group, checked((ushort)(cursor - 16)));
            var zeros = cursor;
            for (var index = 0; index < slots.Count; index++)
                if (slots[index] < 0) { BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(cursor), checked((ushort)(index * 4))); cursor += 2; }
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(10), checked((ushort)(cursor - zeros)));
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(12), checked((ushort)(slots.Count / 4)));
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(14), checked((ushort)group.Length));
            groups.Add(group); slots.Clear();
        }
    }

    private static byte[] EncodeNormal(TieVertexNormal source)
    {
        var raw = new Vector3(source.X, source.Y, source.Z);
        if (raw.LengthSquared() < 1e-12f) return new byte[8];
        var normal = -Vector3.Normalize(raw);
        var bytes = new byte[8];
        bytes[0] = unchecked((byte)(sbyte)Math.Clamp(MathF.Round(normal.X * 127), -127, 127));
        bytes[1] = unchecked((byte)(sbyte)Math.Clamp(MathF.Round(normal.Y * 127), -127, 127));
        bytes[2] = unchecked((byte)(sbyte)Math.Clamp(MathF.Round(normal.Z * 127), -127, 127));
        bytes[5] = 127;
        var azimuth = (int)MathF.Round(MathF.Atan2(-normal.Y, -normal.X) * 256 / (2 * MathF.PI)) & 255;
        var elevation = (int)MathF.Round(MathF.Asin(Math.Clamp(-normal.Z, -1, 1)) * 256 / (2 * MathF.PI)) & 255;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), checked((ushort)(azimuth | elevation << 8)));
        return bytes;
    }

    private static void WriteBounds(byte[] bytes, TieClass source)
    {
        var vertices = source.PacketDataBlocks.SelectMany(block => block.DecodedVertices).ToArray();
        if (vertices.Length == 0) return;
        var min = new Vector3(vertices.Min(vertex => vertex.X), vertices.Min(vertex => vertex.Y), vertices.Min(vertex => vertex.Z));
        var max = new Vector3(vertices.Max(vertex => vertex.X), vertices.Max(vertex => vertex.Y), vertices.Max(vertex => vertex.Z));
        for (var index = 0; index < 8; index++)
        {
            var offset = 0x80 + index * 16;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), (index & 1) != 0 ? min.X : max.X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 4), (index & 2) != 0 ? min.Y : max.Y);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 8), (index & 4) != 0 ? max.Z : min.Z);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 12), 1);
        }
    }

    private static int Align4(int value) => checked((value + 3) & ~3);
    private static int Align16(int value) => checked((value + 15) & ~15);
    private static void Align(Stream stream) { while (stream.Position % 16 != 0) stream.WriteByte(0); }
}
