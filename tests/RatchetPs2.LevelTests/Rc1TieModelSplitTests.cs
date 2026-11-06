using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Ties;
using RatchetPs2.Games.RC1.Ties;
using RatchetPs2.Sdk;

internal static class Rc1TieModelSplitTests
{
    public static void Run()
    {
        var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LevelContent", "rc1-oversized-tie.bin"));
        var original = source.ToArray();
        try
        {
            LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, source, 0x1234);
            throw new Exception("Oversized source unexpectedly fits in one native Tie.");
        }
        catch (NotSupportedException) { }
        var parts = LevelAssetModelConverter.ConvertParts(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, source, 0x1234);
        Check(parts.Count > 1, "oversized model splits");
        var sourceTie = TieClassReader.Read(source, TieClassReadOptions.ForGameProfile(Rc1TieGameProfile.Default));
        var ties = parts.Select(bytes => TieClassReader.Read(bytes)).ToArray();
        for (var lod = 0; lod < 3; lod++)
        {
            Check(ties.Sum(tie => tie.LodTopologies[lod].Triangles.Count) == sourceTie.LodTopologies[lod].Triangles.Count,
                "all LOD triangles survive exactly once");
            foreach (var tie in ties)
            {
                Check(tie.PacketTables[lod].Packets.Count is > 0 and <= 255, "each part retains every LOD within packet limits");
                var operations = tie.RgbaRemapOperations.Where(operation => operation.LodIndex == lod).ToArray();
                Check(operations.Select(operation => operation.GroupIndex).Distinct().Count() <= 31, "native lighting group limit retained");
                Check(operations.Max(operation => operation.TargetCacheSlot) < tie.Header.CacheSizes[lod] * 4,
                    "lighting remaps address the part cache");
                foreach (var block in tie.PacketDataBlocks.Where(block => block.LodIndex == lod))
                    GcTieVuUploadAssertions.Validate(block);
            }
        }
        Check(source.SequenceEqual(original), "splitting preserves its input");
        Check(parts.All(part => part.AsSpan(0x10, 12).SequenceEqual(parts[0].AsSpan(0x10, 12))
            && part.AsSpan(0x80, 0x80).SequenceEqual(parts[0].AsSpan(0x80, 0x80))), "parts share LOD distances and bounds");
        var rebased = LevelAssetModelConverter.RebaseClass(GameId.UYA, FrontendAssetKind.Tie, parts[1], 0x9000);
        Check((ushort)TieClassReader.Read(rebased).Header.OClass == 0x9000
            && (ushort)ties[1].Header.OClass == 0x1234, "part rebasing preserves the original model");
        var small = Rc1TieLodLightingTests.Fixture(true);
        Check(LevelAssetModelConverter.ConvertParts(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, small, 0x1234).Single()
            .SequenceEqual(LevelAssetModelConverter.Convert(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, small, 0x1234)),
            "ordinary models remain byte-identical");
        try
        {
            LevelAssetModelConverter.ConvertParts(GameId.RC1, GameId.UYA, FrontendAssetKind.Tie, source, 0x1234, new CancellationToken(true));
            throw new Exception("Split conversion ignored cancellation.");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("Oversized RC1 Ties retain all geometry, lighting caches and three LODs across native model parts.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
