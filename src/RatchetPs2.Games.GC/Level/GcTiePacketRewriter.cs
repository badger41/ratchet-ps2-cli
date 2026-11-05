using System.Buffers.Binary;
using RatchetPs2.Games.UYA.Ties;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.GC.Level;

internal enum GcTiePacketLayout { Current, OriginalSplit, MissingFatPhase, PreviousFacing, CoincidentFacing, UnpaddedStrips }

internal static class GcTiePacketRewriter
{
    public static byte[] Rewrite(byte[] source, byte[] patched, TieClass tie,
        IReadOnlyDictionary<int, IReadOnlyList<TieTriangle>> facing,
        IReadOnlyDictionary<int, IReadOnlyList<TieTriangle>> partitionFacing,
        IReadOnlySet<(int Lod, int Packet)> mixedPackets, CancellationToken token, GcTiePacketLayout layout = GcTiePacketLayout.Current)
    {
        if (tie.Header.AmbientRgbaOffset != 0)
            throw new NotSupportedException("GC tie splitting cannot relocate an inline ambient-color buffer.");
        var packets = new List<UyaTieRebuiltPacket>[3];
        for (var lod = 0; lod < 3; lod++)
        {
            packets[lod] = [];
            var colorBase = 0;
            var topology = tie.LodTopologies[lod];
            foreach (var packet in tie.PacketTables[lod].Packets)
            {
                token.ThrowIfCancellationRequested();
                var block = tie.PacketDataBlocks.Single(value => value.LodIndex == lod && value.PacketIndex == packet.PacketIndex);
                var strips = topology.Strips.Where(value => value.PacketIndex == packet.PacketIndex).ToArray();
                if (!mixedPackets.Contains((lod, packet.PacketIndex)))
                {
                    var colors = new int[(packet.RgbaCount + 3) & ~3];
                    Array.Fill(colors, -1);
                    for (var index = 0; index < packet.RgbaCount; index++) colors[index] = colorBase + index;
                    packets[lod].Add(new(source.AsSpan(checked((int)tie.PacketTables[lod].Offset) + packet.PacketIndex * 16, 16).ToArray(),
                        patched.AsSpan(block.Offset, block.Length).ToArray(), colors,
                        strips.Sum(strip => strip.LogicalVertexCount), strips.Sum(strip => strip.TriangleCount), strips.Length));
                }
                else
                {
                    if (block.DecodedVertices.Count != packet.RgbaCount)
                        throw new InvalidDataException("GC tie upload vertices do not match its color-cache span.");
                    var runs = strips.SelectMany(strip => Split(topology, strip,
                        block.StripControls[strip.PacketStripIndex], facing[lod], partitionFacing[lod])).ToArray();
                    packets[lod].AddRange(UyaTiePacketWriter.Build(source, packet, block, colorBase, runs, layout switch
                    {
                        GcTiePacketLayout.OriginalSplit => UyaTiePacketLayout.OriginalSplit,
                        GcTiePacketLayout.MissingFatPhase => UyaTiePacketLayout.MissingFatPhase,
                        _ => UyaTiePacketLayout.Current,
                    }));
                }
                colorBase += (packet.RgbaCount + 3) & ~3;
            }
            // UYA NTSC-U reads this with LBU (004c9794) and compares the 16-byte table end (004c98ac).
            if (packets[lod].Count > byte.MaxValue)
                throw new InvalidDataException($"GC tie LOD {lod} needs {packets[lod].Count} UYA packets, over the 255-packet limit.");
        }
        using var output = new MemoryStream();
        var prefix = checked((int)tie.PacketTables.Where(table => table.Count > 0).Min(table => table.Offset));
        if (prefix < 0x80) throw new InvalidDataException("GC tie packet table overlaps its header.");
        output.Write(source.AsSpan(0, prefix));
        var tables = new int[3];
        for (var lod = 0; lod < 3; lod++)
        {
            tables[lod] = checked((int)output.Position);
            output.Write(new byte[packets[lod].Count * 16]);
        }
        var dataOffsets = new List<int>[3];
        for (var lod = 0; lod < 3; lod++)
        {
            dataOffsets[lod] = [];
            foreach (var packet in packets[lod])
            {
                token.ThrowIfCancellationRequested();
                dataOffsets[lod].Add(checked((int)output.Position));
                output.Write(packet.Bytes);
            }
        }
        var normalsOffset = checked((int)output.Position);
        output.Write(source.AsSpan(checked((int)tie.Header.VertexNormalsOffset), tie.Header.VertexNormalsCount * 8));
        Align(output);
        var remaps = new int[3];
        for (var lod = 0; lod < 3; lod++)
        {
            token.ThrowIfCancellationRequested();
            if (packets[lod].Count == 0) continue;
            remaps[lod] = checked((int)output.Position - normalsOffset);
            output.Write(GcTieColorRemapWriter.Write(source, tie, lod, packets[lod]));
        }
        var glow = new int[3];
        for (var lod = 0; lod < 3; lod++)
        {
            var bytes = GcTieColorRemapWriter.WriteGlowTargets(source, tie, lod, packets[lod]);
            if (bytes is null) continue;
            glow[lod] = checked((int)output.Position - normalsOffset);
            output.Write(bytes);
        }
        var shadersOffset = checked((int)output.Position);
        output.Write(source.AsSpan(checked((int)tie.Header.ShadersOffset)));
        if (output.Length > StaticModelNativeLimits.MaximumModelBytes)
            throw new InvalidDataException("GC tie conversion exceeds the UYA model size limit.");
        var result = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x34), normalsOffset);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x1c), shadersOffset);
        for (var lod = 0; lod < 3; lod++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(lod * 4), tables[lod]);
            result[12 + lod] = checked((byte)packets[lod].Count);
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x24 + lod * 2),
                checked((short)(packets[lod].Sum(packet => packet.SourceColorSlots.Length) / 4)));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x2a + lod * 2), checked((ushort)remaps[lod]));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x78 + lod * 2), checked((ushort)glow[lod]));
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x60 + lod * 8),
                checked((short)packets[lod].Sum(packet => packet.LogicalVertices)));
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x62 + lod * 8),
                checked((short)packets[lod].Sum(packet => packet.Triangles)));
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0x64 + lod * 8),
                checked((short)packets[lod].Sum(packet => packet.Strips)));
            for (var index = 0; index < packets[lod].Count; index++)
            {
                var record = packets[lod][index].Record;
                BinaryPrimitives.WriteInt32LittleEndian(record, dataOffsets[lod][index] - tables[lod]);
                record.CopyTo(result, tables[lod] + index * 16);
            }
        }
        return result;
    }

    private static IEnumerable<UyaTieStripRun> Split(TieLodTopology topology, TieTriangleStrip strip,
        TiePacketStripControl control, IReadOnlyList<TieTriangle> facing, IReadOnlyList<TieTriangle> partitionFacing)
    {
        var phases = GcLevelAssetModelConverter.ResolveStripFacing(topology, strip, facing);
        var partitions = GcLevelAssetModelConverter.ResolveStripFacing(topology, strip, partitionFacing);
        FillDegeneratePhases(phases);
        FillDegeneratePhases(partitions);
        var start = 0;
        while (start < phases.Length)
        {
            var end = start + 1;
            while (end < phases.Length && phases[end] == phases[start]
                && partitions[end] == partitions[start] && end - start < 62) end++;
            var vertices = strip.LogicalVertices.Skip(start).Take(end - start + 2).ToList();
            // UYA's culling loop consumes two triangles per iteration, including degenerate padding.
            if (vertices.Count % 2 != 0) vertices.Add(vertices[^1]);
            var reverse = ((strip.Flags & 0x20) != 0) ^ phases[start]!.Value ^ ((start & 1) != 0);
            yield return new(vertices.ToArray(), strip.ShaderIndex
                ?? throw new InvalidDataException("GC strip has no resolved shader."),
                (byte)((strip.Flags & ~0x20) | (reverse ? 0x20 : 0)), control.ControlData1);
            start = end;
        }
    }

    private static void FillDegeneratePhases(bool?[] phases)
    {
        var current = phases.FirstOrDefault(value => value.HasValue) ?? false;
        for (var index = 0; index < phases.Length; index++)
        {
            if (phases[index] is { } phase) current = phase;
            else phases[index] = current;
        }
    }

    private static void Align(MemoryStream stream)
    {
        var size = checked((int)((16 - stream.Position % 16) % 16));
        stream.Write(new byte[size]);
    }
}
