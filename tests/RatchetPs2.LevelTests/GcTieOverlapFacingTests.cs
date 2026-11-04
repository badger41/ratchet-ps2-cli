using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Ties;
using RatchetPs2.Sdk;

internal static class GcTieOverlapFacingTests
{
    public static void Run()
    {
        var asset = new StaticModelCanonicalAsset(StaticModelCanonicalFormat.Version,
            [new(0, 0, "overlapping", 0, [0f, 0f, 0f, 4f, 0f, 0f, 0f, 0f, 4f, 1f, 0f, 2f],
                [0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f, 0f],
                [0f, 0f, 1f, 0f, 0f, 1f, 0.25f, 0.5f], null, [0u, 1u, 2u, 0u, 1u, 3u])],
            [new(9729, 9729, 10497, 10497)], [new("texture", 0, 0)],
            [new("overlapping", [1f, 1f, 1f, 1f], 0, 1, 1, "OPAQUE", 0.5f, false)],
            [new("white", 2, 2, Enumerable.Repeat((byte)255, 16).ToArray())], new(0, 0, 0, 4, 0, 4));
        var source = StaticModelConverter.Convert(GameId.UYA, asset, 0xbc).ModelBytes;
        var native = Read(source, GameId.UYA);
        foreach (var block in native.PacketDataBlocks)
        {
            source.AsSpan(block.Offset + 12, 4).Clear();
            foreach (var control in block.StripControls) source[control.Offset + 3] &= 0xdf;
        }
        var preview = Read(source, GameId.GC);
        var facing = TieGltfExporter.ResolveTriangleFacing(preview, 0, TieGameProfile.ForGame(GameId.GC));
        Validate(preview.LodTopologies[0], facing);
        var converted = LevelAssetModelConverter.Convert(GameId.GC, GameId.UYA, FrontendAssetKind.Tie, source, 0xbc);
        GcTieConversionAssertions.Equivalent(source, converted);
        var target = Read(converted, GameId.UYA);
        Validate(target.LodTopologies[0], target.LodTopologies[0].Triangles);
        var previous = converted.ToArray();
        var second = target.LodTopologies[0].Strips.Where(strip => strip.TriangleCount > 0).Skip(1).First();
        var block2 = target.PacketDataBlocks.Single(block => block.LodIndex == 0 && block.PacketIndex == second.PacketIndex);
        previous[block2.StripControls[second.PacketStripIndex].Offset + 3] ^= 0x20;
        var repaired = LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, source, previous, 0xbc, 0xbc);
        if (repaired is null || !repaired.AsSpan().SequenceEqual(converted))
            throw new Exception("Earlier overlapping-face conversion must repair automatically.");
        previous[0x4c] ^= 1;
        if (LevelAssetModelConverter.RepairPreviousStaticConversion(GameId.GC, GameId.UYA,
            FrontendAssetKind.Tie, source, previous, 0xbc, 0xbc) is not null)
            throw new Exception("Overlap repair must preserve independently edited models.");
        Console.WriteLine("GC overlapping coplanar faces retain their authored facing in preview, conversion and repair.");
    }

    private static void Validate(TieLodTopology topology, IReadOnlyList<TieTriangle> triangles)
    {
        var count = 0;
        foreach (var triangle in triangles)
        {
            Vector3 Position(int index)
            {
                var vertex = topology.LogicalVertices[index].DecodedVertex!;
                return GltfCoordinateBasis.FromPs2Position(vertex.X, vertex.Y, vertex.Z);
            }
            var normal = Vector3.Cross(Position(triangle.B) - Position(triangle.A),
                Position(triangle.C) - Position(triangle.A));
            if (normal.LengthSquared() == 0) continue;
            if (normal.Y >= 0) throw new Exception("Overlapping faces must both point along their authored -Y normal.");
            count++;
        }
        if (count != 2) throw new Exception("Overlap fixture must retain both rendered triangles.");
    }

    private static TieClass Read(byte[] bytes, GameId game) => TieClassReader.Read(bytes,
        TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(game)));
}
