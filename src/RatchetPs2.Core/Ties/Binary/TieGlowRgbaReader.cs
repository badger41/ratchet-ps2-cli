namespace RatchetPs2.Core.Ties;

internal static class TieGlowRgbaReader
{
    public static (List<TieGlowRgbaRemap> Remaps, List<TieGlowRgbaVertex> Vertices) Read(
        TieClassHeader header,
        IReadOnlyList<TiePacketDataBlock> packetDataBlocks,
        IReadOnlyList<TieLodTopology> lodTopologies,
        IReadOnlyList<TieRgbaRemapOperation> rgbaRemapOperations)
    {
        var rgba = TieRgba32.FromRaw(header.GlowRgba);
        var recipesByLodAndTarget = new Dictionary<(int LodIndex, int TargetCacheSlot), TieRgbaRemapOperation>();
        for (var i = 0; i < rgbaRemapOperations.Count; i++)
        {
            var operation = rgbaRemapOperations[i];
            var key = (operation.LodIndex, operation.TargetCacheSlot);
            if (!recipesByLodAndTarget.TryGetValue(key, out var current)
                || CompareOperations(operation, current) > 0)
            {
                recipesByLodAndTarget[key] = operation;
            }
        }

        var recipes = new List<TieRgbaRemapOperation>(recipesByLodAndTarget.Count);
        foreach (var operation in recipesByLodAndTarget.Values)
        {
            if (Array.IndexOf(operation.SourceSlots, TieRgbaRemapOperation.ConstantColorSourceSlot) >= 0)
            {
                recipes.Add(operation);
            }
        }
        recipes.Sort(CompareOperations);
        if (recipes.Count == 0)
        {
            return ([], []);
        }

        var vertices = new List<TieGlowRgbaVertex>();
        for (var topologyIndex = 0; topologyIndex < lodTopologies.Count; topologyIndex++)
        {
            var topology = lodTopologies[topologyIndex];
            var recipesByTarget = new Dictionary<int, int>();
            for (var recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
            {
                var recipe = recipes[recipeIndex];
                if (recipe.LodIndex == topology.LodIndex)
                {
                    recipesByTarget[recipe.TargetCacheSlot] = recipeIndex;
                }
            }
            if (recipesByTarget.Count == 0)
            {
                continue;
            }

            var packetUploadLayouts = TieGltfNormalRemapTargetResolver.BuildPacketUploadLayouts(
                packetDataBlocks,
                topology);
            for (var vertexIndex = 0; vertexIndex < topology.LogicalVertices.Count; vertexIndex++)
            {
                var vertex = topology.LogicalVertices[vertexIndex];
                var row = vertex.VertexRow ?? vertex.AddressRow;
                if (row is null
                    || !TieGltfNormalRemapTargetResolver.TryGetPacketUploadTarget(
                        vertex,
                        packetUploadLayouts,
                        out var target)
                    || !recipesByTarget.TryGetValue(target, out var recipeIndex))
                {
                    continue;
                }

                var recipe = recipes[recipeIndex];
                var constantColorSourceCount = 0;
                for (var sourceIndex = 0; sourceIndex < recipe.SourceSlots.Length; sourceIndex++)
                {
                    if (recipe.SourceSlots[sourceIndex] == TieRgbaRemapOperation.ConstantColorSourceSlot)
                    {
                        constantColorSourceCount++;
                    }
                }
                vertices.Add(new TieGlowRgbaVertex
                {
                    RemapIndex = recipeIndex,
                    RemapOffset = recipe.Offset,
                    LodIndex = vertex.LodIndex,
                    PacketIndex = vertex.PacketIndex,
                    StripIndex = vertex.StripIndex,
                    PacketStripIndex = vertex.PacketStripIndex,
                    IndexInStrip = vertex.IndexInStrip,
                    LogicalVertexIndex = vertex.LogicalVertexIndex,
                    VertexRowIndex = row.Index,
                    VertexRowOffset = row.Offset,
                    RawRgba = header.GlowRgba,
                    Rgba = rgba,
                    GlowWeight = constantColorSourceCount / (float)recipe.SourceSlots.Length
                });
            }
        }

        var remaps = new List<TieGlowRgbaRemap>(recipes.Count);
        for (var recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
        {
            remaps.Add(BuildRemap(recipeIndex, recipes[recipeIndex], vertices, header.GlowRgba, rgba));
        }
        vertices.Sort(static (left, right) =>
        {
            var comparison = left.LodIndex.CompareTo(right.LodIndex);
            return comparison != 0 ? comparison : left.LogicalVertexIndex.CompareTo(right.LogicalVertexIndex);
        });
        return (remaps, vertices);
    }

    private static int CompareOperations(TieRgbaRemapOperation left, TieRgbaRemapOperation right)
    {
        var comparison = left.LodIndex.CompareTo(right.LodIndex);
        if (comparison != 0) return comparison;
        comparison = left.GroupIndex.CompareTo(right.GroupIndex);
        if (comparison != 0) return comparison;
        comparison = left.Offset.CompareTo(right.Offset);
        return comparison != 0 ? comparison : left.OperationIndex.CompareTo(right.OperationIndex);
    }

    private static TieGlowRgbaRemap BuildRemap(
        int remapIndex,
        TieRgbaRemapOperation operation,
        IReadOnlyList<TieGlowRgbaVertex> vertices,
        int rawRgba,
        TieRgba32 rgba)
    {
        var packetIndexSet = new HashSet<int>();
        var rowSet = new HashSet<long>();
        var resolvedCount = 0;
        var minOffset = int.MaxValue;
        var maxOffset = int.MinValue;
        var minRowIndex = int.MaxValue;
        var maxRowIndex = int.MinValue;
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            if (vertex.RemapIndex != remapIndex)
            {
                continue;
            }

            resolvedCount++;
            packetIndexSet.Add(vertex.PacketIndex);
            rowSet.Add(((long)vertex.PacketIndex << 32) | (uint)vertex.VertexRowIndex);
            minOffset = Math.Min(minOffset, vertex.VertexRowOffset);
            maxOffset = Math.Max(maxOffset, vertex.VertexRowOffset);
            minRowIndex = Math.Min(minRowIndex, vertex.VertexRowIndex);
            maxRowIndex = Math.Max(maxRowIndex, vertex.VertexRowIndex);
        }

        var packetIndices = packetIndexSet.ToArray();
        Array.Sort(packetIndices);
        var packetIndex = packetIndices.Length == 0 ? (int?)null : packetIndices[0];
        return new TieGlowRgbaRemap
        {
            RemapIndex = remapIndex,
            Offset = operation.Offset,
            RawRgba = rawRgba,
            Rgba = rgba,
            ResolutionKind = resolvedCount > 0
                ? TieGlowRgbaRemapResolutionKind.PacketVertexRowRange
                : TieGlowRgbaRemapResolutionKind.Unresolved,
            ResolvedStartOffset = resolvedCount > 0 ? minOffset : null,
            EndOffset = resolvedCount > 0 ? maxOffset + 0x10 : null,
            LodIndex = operation.LodIndex,
            PacketIndex = packetIndex,
            ResolvedPacketIndex = packetIndex,
            ResolvedPacketIndices = packetIndices,
            StartVertexRowIndex = packetIndices.Length == 1 ? minRowIndex : null,
            EndVertexRowIndexExclusive = packetIndices.Length == 1 ? maxRowIndex + 1 : null,
            ResolvedPacketCount = packetIndices.Length,
            ResolvedVertexRowCount = rowSet.Count,
            ResolvedLogicalVertexCount = resolvedCount
        };
    }
}
