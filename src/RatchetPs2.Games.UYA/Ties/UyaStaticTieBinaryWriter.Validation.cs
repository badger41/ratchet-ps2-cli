using System.Buffers.Binary;
using RatchetPs2.Games.UYA.Textures;
using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Ties;

namespace RatchetPs2.Games.UYA.Ties;

internal static partial class UyaStaticTieBinaryWriter
{
    public static void Validate(
        UyaStaticTieBinaryResult result,
        PreparedModel model,
        IReadOnlyList<BoundMaterial> materials)
    {
        var options = TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA));
        var tie = TieClassReader.Read(result.ModelBytes, options);
        if (tie.Header.TextureCount != materials.Count
            || tie.Header.OClass != unchecked((short)model.TargetClassId)
            || tie.VertexNormals.Count != result.VertexNormalCount
            || tie.Header.PacketTableOffsets.Any(offset => offset % 0x10 != 0)
            || tie.Header.ShadersOffset % 0x10 != 0
            || tie.Header.VertexNormalsOffset % 0x10 != 0
            || (tie.Header.VertexNormalsOffset + tie.Header.RgbaRemapOffsets[0]) % 0x10 != 0
            || tie.Header.AmbientRgbaOffset != 0
            || tie.Header.AmbientSize != Align4(checked((result.VertexNormalCount + 2) * sizeof(ushort)))
            || tie.Header.PacketTableOffsets[0] != HeaderSize
            || tie.Header.PacketTableOffsets[1] != 0
            || tie.Header.PacketTableOffsets[2] != 0
            || tie.Header.RgbaRemapOffsets[1] != tie.Header.RgbaRemapOffsets[2]
            || Enumerable.Range(0, 8).Any(index =>
                BitConverter.ToSingle(result.ModelBytes, 0x80 + index * 0x10 + 0x0c) != 1f)
            || result.ModelBytes.Length % 0x10 != 0)
            throw new InvalidDataException(
                $"Generated UYA TIE failed semantic validation: materials {tie.Header.TextureCount}/{materials.Count}, "
                + $"class {(ushort)tie.Header.OClass:X4}/{model.TargetClassId:X4}, normals {tie.VertexNormals.Count}/{result.VertexNormalCount}, "
                + $"remaps {tie.VertexNormalRemaps.Count}/{model.LogicalVertexCount}.");
        var packets = tie.PacketTables.SelectMany(table => table.Packets).ToArray();
        if (packets.Any(packet => packet.LodIndex != 0)
            || packets.Select(packet => packet.AbsoluteDataOffset).Distinct().Count() != packets.Length)
            throw new InvalidDataException("Generated UYA TIE has invalid LOD0 packet storage.");
        if (packets.Any(packet => packet.RgbaCount != packet.VertexSize)
            || Enumerable.Range(0, 3).Any(lodIndex => tie.Header.CacheSizes[lodIndex] != packets
                .Where(packet => packet.LodIndex == lodIndex)
                .Sum(packet => (packet.RgbaCount + 3) / 4))
            || tie.PacketDataBlocks.Any(block =>
                BinaryPrimitives.ReadInt32LittleEndian(result.ModelBytes.AsSpan(block.Offset + 0x0c)) != -9
                || block.UnpackHeader is not
                {
                    Unknown0: 1,
                    Unknown1: 1,
                    Unknown2: 6,
                    Unknown4: 7,
                    Unknown5: 0,
                    Unknown6: 0,
                    Unknown7: 0,
                    FatVerticesSize: 6,
                    Unknown11: 0,
                }
                || block.StripControls.Count is < 1 or > 15
                || block.StripControls.Any(strip => strip.TokenCount % 2 != 0)
                || block.StripControls.Sum(strip => strip.TokenCount) != block.VertexRows.Count
                || block.StripControls.Any(strip => strip.VuAddress + 1 + strip.TokenCount * 3 > 0xcc)
                || !block.DecodedVertices.Select(vertex => (int)vertex.GsPacketWriteOffset).SequenceEqual(
                    block.StripControls.Reverse().SelectMany(strip =>
                        Enumerable.Range(0, strip.TokenCount).Select(index => strip.VuAddress + 1 + index * 3)))
                || block.StripControls.SelectMany((strip, stripIndex) => strip.Tokens.Select((token, tokenIndex) =>
                    token != (stripIndex == 0 && tokenIndex == 0 ? 7 : tokenIndex == 0 ? 0xfc : 3))).Any(value => value)))
            throw new InvalidDataException("Generated UYA TIE packet metadata does not match the runtime VIF envelope.");
        for (var lodIndex = 0; lodIndex < 3; lodIndex++)
        {
            var topology = tie.LodTopologies[lodIndex];
            var active = lodIndex == 0;
            if (topology.LogicalVertexCount != (active ? model.LogicalVertexCount : 0)
                || topology.TriangleCount != (active ? model.NativeTriangleCount : 0)
                || topology.StripCount != (active ? model.StripCount : 0)
                || topology.UnresolvedLogicalVertexCount != 0)
                throw new InvalidDataException($"Generated UYA TIE LOD {lodIndex} failed topology semantic validation.");
        }
        var lod0 = tie.LodTopologies[0];
        var sphereCenter = new Vector3(
            tie.Header.BoundingSphere.X,
            tie.Header.BoundingSphere.Y,
            tie.Header.BoundingSphere.Z) * result.Scale;
        var sphereRadius = tie.Header.BoundingSphere.Radius * result.Scale;
        var unit = result.Scale / 1024f;
        for (var index = 0; index < result.LogicalVertices.Count; index++)
        {
            var actual = lod0.LogicalVertices[index].DecodedVertex
                ?? throw new InvalidDataException($"Generated UYA TIE logical vertex {index} did not reread.");
            var expected = result.LogicalVertices[index];
            if (actual.X != expected.X || actual.Y != expected.Y || actual.Z != expected.Z
                || actual.S != expected.S || actual.T != expected.T || actual.Q != 4096)
                throw new InvalidDataException($"Generated UYA TIE logical vertex {index} changed during semantic reread.");
            var position = new Vector3(expected.X * unit, expected.Y * unit, expected.Z * unit);
            if (Vector3.Distance(position, sphereCenter) > sphereRadius + unit)
                throw new InvalidDataException($"Generated UYA TIE logical vertex {index} lies outside its native bounds.");
            var decodedNormal = DecodePackedNormal(tie.VertexNormals[result.LogicalNormalIndices[index]].Packed);
            if (Vector3.Dot(decodedNormal, -NormalizeOrFallback(expected.Normal, Vector3.UnitY)) < 0.98f)
                throw new InvalidDataException($"Generated UYA TIE logical vertex {index} changed normal during semantic reread.");
        }
        ValidateRemap(result.ModelBytes, tie, result.LogicalNormalIndices);
        if (tie.Shaders.Where((shader, index) =>
                shader.ClampU != materials[index].ClampU || shader.ClampV != materials[index].ClampV).Any())
            throw new InvalidDataException("Generated UYA TIE changed material clamp state during semantic reread.");
        foreach (var binding in result.PrimitiveBindings)
        {
            var expectedBfcDistance = model.Materials[binding.NativeMaterialIndex].DoubleSided
                ? DoubleSidedBfcDistance
                : BackfaceCulledBfcDistance;
            if (packets.Skip(binding.FirstPacket).Take(binding.PacketCount)
                .Any(packet => packet.BfcDistance != expectedBfcDistance))
                throw new InvalidDataException(
                    $"Generated UYA TIE mesh {binding.SourceMeshIndex}, primitive {binding.SourcePrimitiveIndex} changed backface culling during semantic reread.");
            var strips = lod0.Strips.Where(strip => strip.PacketIndex >= binding.FirstPacket
                && strip.PacketIndex < binding.FirstPacket + binding.PacketCount);
            if (strips.Any(strip => strip.ShaderIndex != binding.NativeMaterialIndex))
                throw new InvalidDataException(
                    $"Generated UYA TIE mesh {binding.SourceMeshIndex}, primitive {binding.SourcePrimitiveIndex} changed material mapping during semantic reread.");
        }
        if (!TieClassWriter.Build(tie).AsSpan().SequenceEqual(result.ModelBytes))
            throw new InvalidDataException("Generated UYA TIE was not byte-stable after semantic reread.");
    }

}
