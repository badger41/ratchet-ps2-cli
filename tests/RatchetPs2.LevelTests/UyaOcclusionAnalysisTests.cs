using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Core.Occlusion;
using System.Buffers.Binary;
using System.Text.Json;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Level;

internal static class UyaOcclusionAnalysisTests
{
    public static void Run()
    {
        var grid = Grid();
        var mappings = Mappings();
        var report = UyaOcclusionAnalyzer.Analyze(grid, mappings);
        Check(report.Octants == 2 && report.ReferencedMaskSlots == 2 && report.DistinctMaskContents == 2, "sparse grid cardinality");
        Check(report.BoundsMin!.SequenceEqual([1, 2, 3]) && report.BoundsMax!.SequenceEqual([2, 2, 3]), "native coordinates");
        Check(report.UniqueCornerSamples == 12 && report.UniqueCornerAndCenterSamples == 14, "shared corners retain distinct centers");
        Check(report.NeighborPairs == 1 && report.EqualNeighborPairs == 0 && report.NeighborHammingMean == 2, "neighbor disagreement");
        Check(report.MappedBits == 2 && report.SharedAcrossFamilies == 1 && report.SetUnmappedBits.SequenceEqual([5]), "shared and unassigned bits");
        var tfrag = report.Families[0];
        Check(tfrag.DistinctSecondWords == 1 && tfrag.RepeatedSecondWordsWithDifferentBits == 1, "repeated tfrag metadata is not a unique ID");
        Check(tfrag.VisibleRecordsMean == 1.5 && tfrag.VisibleRecordsP50 == 1 && tfrag.VisibleRecordsP95 == 2, "octant-weighted distribution");
        Check(report.Families[1].AlwaysSetBits == 1 && report.Families[2].VisibleRecordsMax == 0, "valid masks exclude unknown sentinels from statistics");
        Check(report.Families[2].OutOfRangeBitValues.Single() == new UyaOcclusionBitValue(-1, 1), "unknown sentinel remains explicit");
        Check(report.NoEditMappingWriterByteIdentity && report.MappingPadding == 123 && report.MappingTrailingBytes == 3, "opaque header and tail preservation");
        var again = UyaOcclusionAnalyzer.Analyze(grid, mappings);
        Check(JsonSerializer.Serialize(report) == JsonSerializer.Serialize(again), "repeatable serialized report");
        var swapped = grid.ToArray();
        Write16(swapped, 24, 1); Write16(swapped, 26, 0);
        swapped[32] = grid[160]; swapped[160] = grid[32];
        var reordered = UyaOcclusionAnalyzer.Analyze(swapped, mappings);
        Check(report.GridSha256 != reordered.GridSha256 && report.SemanticGridSha256 == reordered.SemanticGridSha256,
            "semantic identity ignores native mask pool order");
        var shared = grid.ToArray();
        Write16(shared, 26, 0);
        var sharedReport = UyaOcclusionAnalyzer.Analyze(shared, mappings);
        Check(sharedReport.ReferencedMaskSlots == 1 && sharedReport.EqualNeighborPairs == 1 && sharedReport.Families[0].VisibleRecordsMean == 2,
            "shared masks count every octant reference");
        var highGrid = grid.ToArray();
        highGrid[32 + 127] |= 128; highGrid[160 + 127] |= 128;
        var highMappings = mappings.ToArray(); Write32(highMappings, 40, 1023);
        var high = UyaOcclusionAnalyzer.Analyze(highGrid, highMappings).Families[2];
        Check(high.AlwaysSetBits == 1 && high.OutOfRangeBitValues.Count == 0, "last native mask bit remains in range");
        var changedMapping = UyaOcclusionMappingsWriter.RemapTiesBySourceIndex(mappings, [0], [99]);
        var changedReport = UyaOcclusionAnalyzer.Analyze(grid, changedMapping);
        Check(changedReport.SemanticGridSha256 == report.SemanticGridSha256 && changedReport.MappingSha256 != report.MappingSha256
            && UyaOcclusionMappingsReader.Read(changedMapping).Ties.Single().OcclusionId == 99,
            "changed native mapping re-reads independently of mask identity");
        var duplicate = grid.ToArray(); duplicate[160] = duplicate[32];
        var duplicateReport = UyaOcclusionAnalyzer.Analyze(duplicate, mappings);
        Check(duplicateReport.ReferencedMaskSlots == 2 && duplicateReport.DistinctMaskContents == 1, "identical bytes may occupy separate slots");
        var sparse = grid.ToArray(); Write16(sparse, 26, ushort.MaxValue);
        Check(UyaOcclusionAnalyzer.Analyze(sparse, mappings).Octants == 1, "missing leaf sentinel");
        var emptyGrid = new byte[8]; Write32(emptyGrid, 0, 8);
        Check(UyaOcclusionAnalyzer.Analyze(emptyGrid, new byte[16]).Octants == 0, "empty contract");
        Throws<InvalidDataException>(() => UyaOcclusionGridReader.Read(grid, maximumOctants: 1));
        var sparseWork = new byte[131104];
        Write32(sparseWork, 0, sparseWork.Length); Write16(sparseWork, 6, 5);
        for (var index = 0; index < 5; index++) Write16(sparseWork, 8 + index * 2, 5);
        Write16(sparseWork, 22, ushort.MaxValue);
        Throws<InvalidDataException>(() => UyaOcclusionGridReader.Read(sparseWork, maximumOctants: 1));
        var malformedAssets = new byte[4096];
        Write32(malformedAssets, 0, UyaLevelConstants.LevelWadHeaderSize);
        Write32(malformedAssets, 0x10, 1); Write32(malformedAssets, 0x14, 1);
        Write32(malformedAssets, 2048 + 8, 0x80); Write32(malformedAssets, 2048 + 12, 0xc0);
        Write32(malformedAssets, 2048 + 0x48, 0x200); Write32(malformedAssets, 2048 + 0x4c, 0x200);
        Write32(malformedAssets, 2048 + 0x80 + 0x0c, 0x10);
        Write32(malformedAssets, 2048 + 0x80 + 0x18, int.MaxValue);
        Throws<InvalidDataException>(() => UyaOcclusionLevelAnalyzer.Analyze(malformedAssets));
        Write32(malformedAssets, 2048 + 0x80 + 0x18, 1);
        Write32(malformedAssets, 2048 + 0x80 + 0x1c, 0x1000);
        Throws<InvalidDataException>(() => UyaOcclusionLevelAnalyzer.Analyze(malformedAssets));
        Throws<InvalidDataException>(() => UyaOcclusionAnalyzer.Analyze(grid.AsSpan(0, 40), mappings));
        var excessive = new byte[16]; Write32(excessive, 0, UyaOcclusionAnalyzer.MaximumMappingRecords + 1);
        Throws<InvalidDataException>(() => UyaOcclusionAnalyzer.Analyze(grid, excessive));
        Write32(excessive, 0, int.MaxValue); Write32(excessive, 4, int.MaxValue);
        Throws<InvalidDataException>(() => UyaOcclusionMappingsReader.Read(excessive));
        Throws<OperationCanceledException>(() => UyaOcclusionAnalyzer.Analyze(grid, mappings, new CancellationToken(true)));
        Throws<OperationCanceledException>(() => UyaOcclusionGridReader.Read(grid, maximumOctants: 100, cancellationToken: new CancellationToken(true)));
        Throws<OperationCanceledException>(() => UyaOcclusionMappingsReader.Read(mappings, maximumRecords: 100, cancellationToken: new CancellationToken(true)));
        VerifySourceLinks();
        VerifyTerrainLinks();
        VerifyReportPreservation();
        Console.WriteLine("UYA occlusion analysis checks passed");
    }

    private static void VerifySourceLinks()
    {
        var rows = new UyaOcclusionMappings([], [new(0, 78), new(1, 77), new(-1, 77)], [new(5, 500)], 0, []);
        var transform = new UyaInstanceTransform(default, default, default, default);
        var ties = new UyaTieInstances(2, [], [new(0, transform, [], OcclusionId: 77), new(0, transform, [], OcclusionId: 78)], []);
        var report = UyaOcclusionSourceLinker.Link(rows, null, ties, null);
        Check(report.Links.Where(link => link.SourceKey == "tie:instance:0").Select(link => link.MappingIndex).SequenceEqual([1, 2]),
            "native IDs resolve independently of row order and retain negative alias rows");
        Check(report.AmbiguousSources.SequenceEqual(["tie:instance:0"]) && report.UnresolvedMappings.SequenceEqual(["moby:mapping:0"]),
            "ambiguous and missing joins are explicit");
        Check(report.Links.Single(link => link.SourceKey == "tie:instance:1").MappingIndex == 0, "source keys use source indices");
        Throws<OperationCanceledException>(() => UyaOcclusionSourceLinker.Link(rows, null, ties, null, cancellationToken: new CancellationToken(true)));
    }


    private static void VerifyTerrainLinks()
    {
        var rows = new UyaOcclusionMappings([new(0, 7), new(1, 9), new(2, 11)], [], [], 0, []);
        var primary = Terrain(Chunk(0, 1, 9), Chunk(1, 0, 7));
        var chunks = new Dictionary<int, TfragTerrain> { [1] = Terrain(Chunk(0, 2, 11)) };
        var result = UyaOcclusionSourceLinker.Link(rows, primary, null, null, chunks);
        Check(result.TfragTriangleCountMatches == 3 && result.TfragTriangleCountMismatches == 0
            && result.UnresolvedMappings.Count == 0, "primary and streamed groups cover the shared mapping domain");
        Check(result.Links.Single(link => link.SourceKey == "tfrag:primary:group:0").MappingIndex == 1,
            "tfrag record selects its mapping row independently of group order");
        Check(result.Links.Single(link => link.SourceKey == "tfrag:chunk:1:group:0").MappingIndex == 2,
            "streamed source keys retain their chunk domain");
        var uncertain = Terrain(Chunk(0, 0, 99), Chunk(1, -1, 7), Chunk(2, 1, 9) with { OcclusionIndexStash = 1 });
        var uncertainResult = UyaOcclusionSourceLinker.Link(rows, uncertain, null, null);
        Check(uncertainResult.TfragTriangleCountMismatches == 1 && uncertainResult.TfragStashGroups == 1
            && uncertainResult.UnmappedSources.Count == 2, "metadata disagreements and stash/negative indices remain explicit");
    }

    private static TfragTerrain Terrain(params TfragChunk[] chunks) => new(0, 0, chunks.Length, 0, chunks.Length, chunks, []);

    private static TfragChunk Chunk(int index, short mapping, byte triangles) => new(
        Index: index, RecordOffset: 0, BoundingSphere: new(0, 0, 0, 0), DataOffsetRaw: 0, DataOffset: 0, DataLength: 0,
        Lod2Offset: 0, SharedOffset: 0, Lod1Offset: 0, Lod0Offset: 0, TextureOffset: 0, RgbaOffset: 0,
        CommonSize: 0, Lod2Size: 0, Lod1Size: 0, Lod0Size: 0, Lod2RgbaCount: 0, Lod1RgbaCount: 0, Lod0RgbaCount: 0,
        BaseOnly: 0, TextureCount: 0, RgbaSize: 0, RgbaVerticesLocation: 0, OcclusionIndexStash: 0, MSphereCount: 0,
        Flags: 0, MSphereOffset: 0, LightOffset: 0, LightVertexStartOffset: 0, DirectionalLightsOne: 0,
        DirectionalLightsUpdated: 0, PointLights: 0, CubeOffset: 0, OcclusionIndex: mapping, VertexCount: 0,
        TriangleCount: triangles, MipDistance: 0, TextureEntries: [], RgbaEntries: []);

    private static void VerifyReportPreservation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"occlusion-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "report.json");
            File.WriteAllText(output, "previous good report");
            var report = new UyaOcclusionCorpusReport(2, "source", 0, "test", "hash", "test", []);
            Throws<OperationCanceledException>(() => UyaOcclusionQualification.Write(report, output, new CancellationToken(true)));
            Throws<InvalidDataException>(() => UyaOcclusionQualification.Write(report with { SchemaVersion = 1 }, output));
            Check(File.ReadAllText(output) == "previous good report", "failure and cancellation preserve the prior report");
            var blocked = Path.Combine(directory, "existing-directory");
            Directory.CreateDirectory(blocked);
            Throws<IOException>(() => UyaOcclusionQualification.Write(report, blocked));
            Check(!Directory.EnumerateFiles(directory, ".occlusion-*.tmp").Any(), "failed publication removes its temporary file");
            UyaOcclusionQualification.Write(report, output);
            using var document = JsonDocument.Parse(File.ReadAllBytes(output));
            Check(document.RootElement.GetProperty("SchemaVersion").GetInt32() == 2, "validated successful report publication");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static byte[] Grid()
    {
        var bytes = new byte[32 + 256];
        Write32(bytes, 0, 32); Write16(bytes, 4, 3); Write16(bytes, 6, 1); Write16(bytes, 8, 3);
        Write16(bytes, 12, 2); Write16(bytes, 14, 1); Write16(bytes, 16, 5);
        Write16(bytes, 20, 1); Write16(bytes, 22, 2); Write16(bytes, 24, 0); Write16(bytes, 26, 1);
        bytes[32] = 0x23; bytes[160] = 2;
        return bytes;
    }

    internal static byte[] Mappings()
    {
        var bytes = new byte[51];
        Write32(bytes, 0, 2); Write32(bytes, 4, 1); Write32(bytes, 8, 1); Write32(bytes, 12, 123);
        Write32(bytes, 16, 0); Write32(bytes, 20, 7);
        Write32(bytes, 24, 1); Write32(bytes, 28, 7);
        Write32(bytes, 32, 1); Write32(bytes, 36, 77);
        Write32(bytes, 40, -1); Write32(bytes, 44, 88);
        bytes[48] = 1; bytes[49] = 2; bytes[50] = 3;
        return bytes;
    }

    private static void Write16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void Write32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException($"UYA occlusion: {message}");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}
