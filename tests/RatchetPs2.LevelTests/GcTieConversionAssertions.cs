using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Ties;

internal static class GcTieConversionAssertions
{
    public static void Equivalent(byte[] sourceBytes, byte[] convertedBytes)
    {
        var source = Read(sourceBytes, GameId.GC);
        var converted = Read(convertedBytes, GameId.UYA);
        Expect(source.Header.OClass == converted.Header.OClass, "source class retained");
        Expect(source.Header.Scale == converted.Header.Scale, "quantization scale retained");
        Expect(source.Header.VertexNormalsCount == converted.Header.VertexNormalsCount
            && sourceBytes.AsSpan((int)source.Header.VertexNormalsOffset, source.Header.VertexNormalsCount * 8)
                .SequenceEqual(convertedBytes.AsSpan((int)converted.Header.VertexNormalsOffset, converted.Header.VertexNormalsCount * 8)),
            "normal palette is byte-identical");
        Expect(source.Header.TextureCount == converted.Header.TextureCount
            && sourceBytes.AsSpan((int)source.Header.ShadersOffset, source.Header.TextureCount * 0x50)
                .SequenceEqual(convertedBytes.AsSpan((int)converted.Header.ShadersOffset, converted.Header.TextureCount * 0x50)),
            "shaders and texture bindings are byte-identical");
        for (var lod = 0; lod < 3; lod++)
        {
            var wanted = Faces(sourceBytes, source, lod, resolveFacing: true);
            var actual = Faces(convertedBytes, converted, lod, resolveFacing: false);
            Expect(wanted.SequenceEqual(actual),
                $"LOD {lod} preserves face order, oriented geometry, vertex attributes, lighting recipes, glow targets, BFC distances and UV passes"
                + $" (faces {wanted.Count}/{actual.Count})");
            Expect(converted.PacketTables[lod].Packets.Count <= byte.MaxValue, "unsigned packet count stays within UYA limits");
            Expect(converted.Header.CacheSizes[lod] == converted.PacketTables[lod].Packets.Sum(packet => (packet.RgbaCount + 3) / 4),
                "lighting cache fits every packet upload");
            if (converted.PacketTables[lod].Count == 0) continue;
            var remap = (int)converted.Header.VertexNormalsOffset + converted.Header.RgbaRemapOffsets[lod];
            Expect(convertedBytes[remap + 31] == 0, "lighting remap group table has its runtime terminator");
        }
        foreach (var block in converted.PacketDataBlocks)
        {
            GcTieVuUploadAssertions.Validate(block);
            var packet = converted.PacketTables[block.LodIndex].Packets[block.PacketIndex];
            Expect(BinaryPrimitives.ReadInt32LittleEndian(block.Bytes.AsSpan(12)) == -(4 + 5 * packet.ShaderCount),
                "VU culling setup locates the strip controls");
            Expect(packet.RgbaCount == block.DecodedVertices.Count, "dinky/fat counts fit the RGBA upload");
            Expect(block.StripControls.All(strip => strip.TokenCount % 2 == 0
                && strip.VuAddress + 1 + strip.TokenCount * 3 <= 0xcc), "VU paired loops terminate inside the GS output buffer");
        }
    }

    private static List<string> Faces(byte[] bytes, TieClass tie, int lod, bool resolveFacing)
    {
        var topology = tie.LodTopologies[lod];
        var faces = new List<string>();
        if (topology.TriangleCount == 0) return faces;
        var triangles = resolveFacing
            ? TieGltfExporter.ResolveTriangleFacing(tie, lod, TieGameProfile.ForGame(GameId.GC)) : topology.Triangles;
        var bases = new Dictionary<int, int>();
        var colorBase = 0;
        foreach (var packet in tie.PacketTables[lod].Packets)
        {
            bases[packet.PacketIndex] = colorBase;
            colorBase += (packet.RgbaCount + 3) & ~3;
        }
        var recipes = tie.RgbaRemapOperations.Where(value => value.LodIndex == lod).GroupBy(value => value.TargetCacheSlot)
            .ToDictionary(group => group.Key, group => Recipe(bytes, group.OrderBy(value => value.Offset).Last()));
        var glow = new HashSet<int>();
        if (tie.Header.UnknownOffsets78[lod] != 0)
        {
            var offset = (int)tie.Header.VertexNormalsOffset + tie.Header.UnknownOffsets78[lod];
            var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
            for (var index = 0; index < count; index++)
                glow.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 2 + index * 2)) / 4);
        }
        var vertexKeys = new string[topology.LogicalVertexCount];
        foreach (var strip in topology.Strips)
        {
            var packet = tie.PacketTables[lod].Packets[strip.PacketIndex];
            var block = tie.PacketDataBlocks.Single(value => value.LodIndex == lod && value.PacketIndex == strip.PacketIndex);
            var uvHeader = "";
            if (packet.MultipassOffset > 0)
            {
                var header = bytes.AsSpan(packet.AbsoluteDataOffset + packet.MultipassOffset * 16, 48).ToArray();
                header.AsSpan(32, 4).Clear();
                uvHeader = Convert.ToHexString(header);
            }
            foreach (var logical in strip.LogicalVertices)
            {
                var vertex = logical.DecodedVertex ?? throw new Exception("unresolved converted vertex");
                var attributes = vertex.Bytes.ToArray();
                attributes.AsSpan(6, 2).Clear();
                attributes.AsSpan(attributes.Length - 2, 2).Clear();
                var color = bases[logical.PacketIndex] + vertex.Index;
                Expect(recipes.ContainsKey(color), $"lighting recipe covers cache slot {color}");
                var uv = "";
                if (packet.MultipassOffset > 0)
                {
                    var oldIndex = block.StripControls.Take(strip.PacketStripIndex).Sum(value => value.TokenCount) + logical.IndexInStrip;
                    var offset = packet.AbsoluteDataOffset + packet.MultipassOffset * 16 + 48 + oldIndex % (packet.MultipassUvSize * 4) * 4;
                    uv = Convert.ToHexString(bytes.AsSpan(offset, 4));
                }
                vertexKeys[logical.LogicalVertexIndex] = $"{vertex.Kind}:{Convert.ToHexString(attributes)}:{recipes[color]}:{glow.Contains(color)}:{uvHeader}:{uv}";
            }
        }
        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            var original = topology.Triangles[index];
            if (Degenerate(topology, original)) continue;
            var strip = topology.Strips[triangle.StripIndex];
            var packet = tie.PacketTables[lod].Packets[strip.PacketIndex];
            if (packet.BfcDistance == 0) triangle = original;
            var keys = new[] { vertexKeys[triangle.A], vertexKeys[triangle.B], vertexKeys[triangle.C] };
            if (packet.BfcDistance == 0) Array.Sort(keys, StringComparer.Ordinal);
            var rotations = Enumerable.Range(0, 3).Select(start => string.Join('/', Enumerable.Range(0, 3).Select(i => keys[(start + i) % 3])));
            var key = $"{strip.ShaderIndex}:{packet.BfcDistance}:{packet.PassFlags}:" + rotations.Min(StringComparer.Ordinal);
            faces.Add(key);
        }
        return faces;
    }

    private static string Recipe(byte[] source, TieRgbaRemapOperation operation)
    {
        var size = operation.Kind is TieRgbaRemapOperationKind.WeightedAverage2To1To1 or TieRgbaRemapOperationKind.Average4 ? 8 : 4;
        var bytes = source.AsSpan(operation.Offset, size).ToArray();
        if (operation.Kind is TieRgbaRemapOperationKind.Average2 or TieRgbaRemapOperationKind.WeightedAverage3To1)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, BinaryPrimitives.ReadUInt32LittleEndian(bytes) & ~0x3ffcu);
        else
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(size - 2),
                (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(size - 2)) & ~0x3ffc));
        return $"{operation.Kind}:{Convert.ToHexString(bytes)}";
    }

    private static bool Degenerate(TieLodTopology topology, TieTriangle triangle)
    {
        var a = topology.LogicalVertices[triangle.A].DecodedVertex!;
        var b = topology.LogicalVertices[triangle.B].DecodedVertex!;
        var c = topology.LogicalVertices[triangle.C].DecodedVertex!;
        long x1 = b.X - a.X, y1 = b.Y - a.Y, z1 = b.Z - a.Z;
        long x2 = c.X - a.X, y2 = c.Y - a.Y, z2 = c.Z - a.Z;
        return y1 * z2 == z1 * y2 && z1 * x2 == x1 * z2 && x1 * y2 == y1 * x2;
    }

    private static TieClass Read(byte[] bytes, GameId game) => TieClassReader.Read(bytes,
        TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(game)));
    private static void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }
}
