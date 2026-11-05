using System.Buffers.Binary;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.UYA.Ties;

internal sealed record UyaTieStripRun(TieLogicalVertex[] Vertices, int Shader, byte Flags, byte ControlData1);
internal sealed record UyaTieRebuiltPacket(byte[] Record, byte[] Bytes, int[] SourceColorSlots,
    int LogicalVertices, int Triangles, int Strips);

internal enum UyaTiePacketLayout { Current, OriginalSplit, MissingFatPhase }

internal static class UyaTiePacketWriter
{
    public static IEnumerable<UyaTieRebuiltPacket> Build(byte[] source, TiePacket packet,
        TiePacketDataBlock block, int colorBase, IReadOnlyList<UyaTieStripRun> runs, UyaTiePacketLayout layout = UyaTiePacketLayout.Current)
    {
        var batch = new List<UyaTieStripRun>();
        foreach (var run in runs)
        {
            if (batch.Count > 0 && !Fits(batch.Append(run)))
            {
                yield return Write(source, packet, block, colorBase, batch, layout);
                batch.Clear();
            }
            batch.Add(run);
            if (!Fits(batch)) throw new InvalidDataException("Source tie strip exceeds UYA packet limits.");
        }
        if (batch.Count > 0) yield return Write(source, packet, block, colorBase, batch, layout);
    }

    private static bool Fits(IEnumerable<UyaTieStripRun> runs)
    {
        var end = 6;
        var shaders = 0;
        var strips = 0;
        var previousShader = -1;
        foreach (var run in runs)
        {
            if (run.Shader != previousShader)
            {
                if (shaders++ > 0) end += 6;
                previousShader = run.Shader;
            }
            end += 1 + run.Vertices.Length * 3;
            strips++;
        }
        return end <= 0xcc && shaders <= 4 && strips <= 15;
    }

    private static UyaTieRebuiltPacket Write(byte[] source, TiePacket original, TiePacketDataBlock block,
        int colorBase, IReadOnlyList<UyaTieStripRun> runs, UyaTiePacketLayout layout)
    {
        var uploads = new List<VertexUpload>();
        var available = new Dictionary<int, int>();
        var drawCount = 0;
        foreach (var vertex in runs.SelectMany(run => run.Vertices))
        {
            var sourceIndex = vertex.DecodedVertex!.Index;
            if (original.MultipassOffset == 0 && available.Remove(sourceIndex, out var previous))
                uploads[previous] = uploads[previous] with { Secondary = drawCount };
            else
            {
                available[sourceIndex] = uploads.Count;
                uploads.Add(new(vertex.DecodedVertex!, drawCount, null));
            }
            drawCount++;
        }
        // Reproduce the old layout only when recognizing an earlier conversion for migration.
        var vertices = layout == UyaTiePacketLayout.OriginalSplit ? uploads.OrderBy(value => value.Vertex.Kind).ToArray()
            : PrepareUploads(uploads, block, layout).ToArray();
        var count = vertices.Length;
        var dinky = vertices.Count(value => value.Vertex.Kind == TiePacketDecodedVertexKind.Dinky);
        var fat = count - dinky;
        var controlCount = 3 + runs.Count;
        var controlSize = (controlCount + 3) / 4;
        var vertexOffset = 2 + controlSize;
        var vertexSize = (dinky * 16 + fat * 24 + 15) / 16;
        var scissorOffset = vertexOffset + vertexSize;
        var scissorSize = (drawCount + 1 + 15) / 16;
        var multipassOffset = original.MultipassOffset > 0 ? scissorOffset + scissorSize : 0;
        var multipassSize = multipassOffset > 0 ? (drawCount + 3) / 4 : 0;
        if (multipassOffset > 0 && (original.PassFlags != 1 || original.MultipassUvSize * 4 <
            block.StripControls.Sum(strip => strip.TokenCount)))
            throw new NotSupportedException("Source tie strip splitting requires a complete texture-matrix UV stream.");
        var bytes = new byte[(multipassOffset > 0 ? multipassOffset + 3 + multipassSize : scissorOffset + scissorSize) * 16];
        var record = new byte[16];
        record[5] = original.BfcDistance;
        record[6] = checked((byte)controlCount);
        record[7] = checked((byte)controlSize);
        record[8] = checked((byte)vertexOffset);
        record[9] = checked((byte)vertexSize);
        record[10] = checked((byte)count);
        record[11] = checked((byte)multipassOffset);
        record[12] = checked((byte)scissorOffset);
        record[13] = checked((byte)scissorSize);
        record[14] = original.PassFlags;
        record[15] = checked((byte)multipassSize);
        source.AsSpan(original.AbsoluteDataOffset + 0x20, 12).CopyTo(bytes.AsSpan(0x20));
        bytes[0x23] = checked((byte)runs.Count);
        bytes[0x28] = checked((byte)(4 + 2 * dinky));
        bytes[0x29] = checked((byte)(6 + 3 * fat));
        bytes[0x2a] = checked((byte)dinky);
        bytes[0x2b] = checked((byte)fat);
        var destinations = new int[drawCount];
        var cursor = 6;
        var logical = 0;
        var shaders = 0;
        var previousShader = -1;
        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var shaderSwitch = previousShader >= 0 && previousShader != run.Shader;
            if (run.Shader != previousShader)
            {
                if (shaders > 0)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan((shaders - 1) * 4), cursor);
                    cursor += 6;
                }
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x10 + shaders * 4), run.Shader * 0x50);
                shaders++;
                previousShader = run.Shader;
            }
            var control = 0x2c + index * 4;
            bytes[control] = checked((byte)run.Vertices.Length);
            bytes[control + 1] = run.ControlData1;
            bytes[control + 2] = checked((byte)cursor);
            bytes[control + 3] = run.Flags;
            for (var vertex = 0; vertex < run.Vertices.Length; vertex++, logical++)
            {
                destinations[logical] = cursor + 1 + vertex * 3;
                bytes[scissorOffset * 16 + logical] = vertex > 0 ? (byte)3
                    : index == 0 ? (byte)7 : shaderSwitch ? (byte)0xf6 : (byte)0xfc;
            }
            cursor += 1 + run.Vertices.Length * 3;
        }
        bytes[scissorOffset * 16 + drawCount] = 0xf6;
        record[4] = checked((byte)shaders);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), -(4 + shaders * 5));
        if (layout != UyaTiePacketLayout.OriginalSplit)
        {
            for (var kind = 0; kind < 2; kind++)
            {
                var group = vertices.Where(value => (value.Vertex.Kind == TiePacketDecodedVertexKind.Dinky ? 0 : 1) == kind);
                var singles = group.Where(value => value.Secondary is null).ToArray();
                var shared = group.Where(value => value.Secondary is not null).ToArray();
                // The second flag skips the entire fat phase, including single writes.
                var skip = kind == 0 || layout == UyaTiePacketLayout.MissingFatPhase
                    ? shared.Length == 0 : fat == 0;
                bytes[0x20 + kind] = skip ? (byte)1 : (byte)0;
                bytes[0x24 + kind * 2] = singles.Length == 0 ? (byte)0
                    : checked((byte)destinations[singles[^(kind == 0 ? 4 : 1)].Primary]);
                bytes[0x25 + kind * 2] = shared.Length == 0 ? (byte)0
                    : checked((byte)destinations[shared[^(kind == 0 ? 3 : 1)].Primary]);
            }
        }
        var colors = new int[(count + 3) & ~3];
        Array.Fill(colors, -1);
        var vertexCursor = vertexOffset * 16;
        for (var index = 0; index < vertices.Length; index++)
        {
            var upload = vertices[index];
            var vertex = upload.Vertex;
            vertex.Bytes.CopyTo(bytes, vertexCursor);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(vertexCursor + 6), checked((ushort)destinations[upload.Primary]));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(vertexCursor + vertex.Bytes.Length - 2),
                upload.Secondary is { } secondary ? checked((ushort)destinations[secondary]) : (ushort)0);
            colors[index] = colorBase + vertex.Index;
            vertexCursor += vertex.Bytes.Length;
        }
        if (multipassOffset > 0)
        {
            source.AsSpan(original.AbsoluteDataOffset + original.MultipassOffset * 16, 48)
                .CopyTo(bytes.AsSpan(multipassOffset * 16));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(multipassOffset * 16 + 32), drawCount);
            // UV1 words follow physical strip order, independently of dinky/fat upload order.
            var stripStarts = block.StripControls.Select(strip => strip.TokenCount).ToArray();
            var uv = multipassOffset * 16 + 48;
            foreach (var vertex in runs.SelectMany(run => run.Vertices))
            {
                var oldIndex = stripStarts.Take(vertex.PacketStripIndex).Sum(value => value) + vertex.IndexInStrip;
                source.AsSpan(original.AbsoluteDataOffset + original.MultipassOffset * 16 + 48 + oldIndex * 4, 4)
                    .CopyTo(bytes.AsSpan(uv));
                uv += 4;
            }
        }
        return new(record, bytes, colors, drawCount, drawCount - runs.Count * 2, runs.Count);
    }

    private static IEnumerable<VertexUpload> PrepareUploads(List<VertexUpload> uploads, TiePacketDataBlock block, UyaTiePacketLayout layout)
    {
        if (uploads.All(value => value.Vertex.Kind != TiePacketDecodedVertexKind.Dinky))
        {
            // Fat-only batches still need the four prefetched dinky vertices; real fat writes replace these placeholders.
            var padding = block.DecodedVertices.First(value => value.Kind == TiePacketDecodedVertexKind.Dinky);
            uploads.Insert(0, new(padding, uploads[0].Primary, null));
        }
        foreach (var group in uploads.GroupBy(value => value.Vertex.Kind).OrderBy(group => group.Key))
        {
            var singles = group.Where(value => value.Secondary is null).ToList();
            var shared = group.Where(value => value.Secondary is not null).ToList();
            var dinky = group.Key == TiePacketDecodedVertexKind.Dinky;
            var minimum = dinky ? 4 : 1;
            while (shared.Count > 0 && (singles.Count < minimum || dinky && shared.Count < 3))
            {
                var vertex = shared[^1];
                shared.RemoveAt(shared.Count - 1);
                singles.Add(vertex with { Secondary = null });
                singles.Add(vertex with { Primary = vertex.Secondary!.Value, Secondary = null });
            }
            if (layout != UyaTiePacketLayout.MissingFatPhase && !dinky && shared.Count == 0)
            {
                // UYA always runs both fat phases; a self-write supplies the shared phase for an unshared tail.
                var last = singles[^1];
                if (singles.Count > 1) singles.RemoveAt(singles.Count - 1);
                shared.Add(last with { Secondary = last.Primary });
            }
            // The dinky pipeline preloads four vertices; repeat a real write to drain a short group safely.
            while (singles.Count < minimum) singles.Add(singles[^1]);
            foreach (var vertex in singles) yield return vertex;
            foreach (var vertex in shared) yield return vertex;
        }
    }

    private sealed record VertexUpload(TiePacketDecodedVertex Vertex, int Primary, int? Secondary);

}
