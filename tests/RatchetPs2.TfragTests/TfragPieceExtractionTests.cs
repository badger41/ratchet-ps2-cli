using System.Buffers.Binary;
using System.Diagnostics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Sdk;

internal static class TfragPieceExtractionTests
{
    private const string SyntheticContentSnapshot = "3b231270be0acb638ab288e7bf9e84c111f786d72f4fcf07d10b1c93095973a0";

    public static void Run(string repoRoot)
    {
        ValidateDuplicateOccurrencesAndDeterminism();
        ValidateMalformedInputAndCancellation();
        ProfileLargestLocalLevel(repoRoot);
    }

    private static void ValidateDuplicateOccurrencesAndDeterminism()
    {
        var primary = new TfragPiecePayload(
            "level_wad\\terrain.bin",
            TfragPieceSourceKind.Primary,
            0,
            TfragBoundaryTests.CreateSyntheticTerrain());
        var chunk = new TfragPiecePayload(
            "level_wad/chunks/chunk1.wad",
            TfragPieceSourceKind.Chunk,
            1,
            TfragBoundaryTests.CreateSyntheticTerrain());
        var first = TfragPieceConverter.Extract([chunk, primary], GameId.UYA);
        var second = TfragPieceConverter.Extract([primary, chunk], GameId.UYA);

        Expect(first.Occurrences.Count == 4, "synthetic extraction should retain every native occurrence");
        Expect(first.Sources.Select(value => (value.SourceKind, value.SourceIndex, value.GroupCount))
                .SequenceEqual([
                    (TfragPieceSourceKind.Primary, 0, 2),
                    (TfragPieceSourceKind.Chunk, 1, 2)]),
            "extraction should retain deterministic source ownership, including group counts");
        Expect(first.Assets.Count == 1, "identical synthetic pieces should deduplicate to one canonical asset");
        Expect(first.Occurrences.Select(value => value.OccurrenceKey).Distinct().Count() == 4,
            "deduplicated pieces should retain distinct occurrence keys");
        Expect(first.Occurrences.All(value => value.ContentId == first.Assets[0].ContentId),
            "deduplicated occurrences should reference the shared canonical content");
        Expect(first.Occurrences.Select(value => (value.Provenance.SourceKind, value.Provenance.GroupIndex))
                .SequenceEqual([
                    (TfragPieceSourceKind.Primary, 0),
                    (TfragPieceSourceKind.Primary, 1),
                    (TfragPieceSourceKind.Chunk, 0),
                    (TfragPieceSourceKind.Chunk, 1)]),
            "occurrences should use stable primary/chunk/group ordering regardless of input order");
        Expect(first.Occurrences[0].Provenance.SourceArchive == "level_wad/terrain.bin",
            "source archive paths should use deterministic project-relative separators");
        Expect(first.Assets[0].ContentId == SyntheticContentSnapshot,
            $"canonical synthetic snapshot changed: {first.Assets[0].ContentId}");

        Expect(first.Assets.Select(value => value.ContentId).SequenceEqual(second.Assets.Select(value => value.ContentId)),
            "canonical asset ordering and IDs should be deterministic");
        Expect(first.Occurrences.Select(value => value.OccurrenceKey)
                .SequenceEqual(second.Occurrences.Select(value => value.OccurrenceKey)),
            "occurrence ordering and keys should be deterministic");
        Expect(first.Assets[0].CanonicalBytes.AsSpan().SequenceEqual(second.Assets[0].CanonicalBytes),
            "canonical bytes should be deterministic");

        var reread = TfragPieceCanonicalCodec.Read(first.Assets[0].CanonicalBytes);
        Expect(reread.Lods.Select(value => value.LodIndex).SequenceEqual([0, 1, 2]),
            "canonical piece should retain all three native LOD slots");
        Expect(reread.Lods.All(value => value.Primitives.Count == 0),
            "empty synthetic native streams should remain empty canonical LODs");

        var corrupt = (byte[])first.Assets[0].CanonicalBytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(corrupt.AsSpan(36, 4), int.MaxValue);
        ExpectInvalidData(() => TfragPieceCanonicalCodec.Read(corrupt),
            "canonical counts must be rejected before allocation");
    }

    private static void ValidateMalformedInputAndCancellation()
    {
        var malformedPayload = TfragBoundaryTests.CreateSyntheticTerrain();
        BinaryPrimitives.WriteInt32LittleEndian(malformedPayload.AsSpan(0x40 + 0x10, 4), 0x40);
        var payloadError = CaptureInvalidData(() => TfragPieceConverter.Extract(
            [new("level_wad/terrain.bin", TfragPieceSourceKind.Primary, 0, malformedPayload)],
            GameId.UYA));
        Expect(payloadError.Message.Contains("level_wad/terrain.bin", StringComparison.Ordinal)
               && payloadError.Message.Contains("primary", StringComparison.Ordinal),
            "native payload failures should identify their archive and source owner");

        var malformedGroup = TfragBoundaryTests.CreateSyntheticTerrain();
        BinaryPrimitives.WriteInt32LittleEndian(
            malformedGroup.AsSpan(0x40, 4),
            BitConverter.SingleToInt32Bits(float.NaN));
        var groupError = CaptureInvalidData(() => TfragPieceConverter.Extract(
            [new("level_wad/chunks/chunk1.wad", TfragPieceSourceKind.Chunk, 1, malformedGroup)],
            GameId.UYA));
        Expect(groupError.Message.Contains("chunk 1", StringComparison.Ordinal)
               && groupError.Message.Contains("group 0", StringComparison.Ordinal),
            "canonical extraction failures should identify their chunk and native group");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ExpectCanceled(() => TfragPieceConverter.Extract(
            [new("level_wad/terrain.bin", TfragPieceSourceKind.Primary, 0,
                TfragBoundaryTests.CreateSyntheticTerrain())],
            GameId.UYA,
            cancellation.Token));
    }

    private static void ProfileLargestLocalLevel(string repoRoot)
    {
        var primaryPath = Path.Combine(repoRoot, "test-assets", "tfrags", "UYA", "level4", "terrain", "terrain.bin");
        var chunkRoot = Path.Combine(repoRoot, "test-assets", "extractions_uya", "level04_iso_world01", "level_wad", "chunks");
        var chunkPaths = new[] { Path.Combine(chunkRoot, "chunk1.wad"), Path.Combine(chunkRoot, "chunk2.wad") };
        if (!File.Exists(primaryPath) || chunkPaths.Any(path => !File.Exists(path)))
        {
            Console.WriteLine("No complete local level04 tfrag fixture found; skipping largest-level extraction profile.");
            return;
        }

        var payloads = new List<TfragPiecePayload>
        {
            new("level_wad/terrain.bin", TfragPieceSourceKind.Primary, 0, File.ReadAllBytes(primaryPath))
        };
        for (var index = 0; index < chunkPaths.Length; index++)
        {
            payloads.Add(new(
                $"level_wad/chunks/chunk{index + 1}.wad",
                TfragPieceSourceKind.Chunk,
                index + 1,
                TfragChunkWadReader.ReadTerrainPayload(File.ReadAllBytes(chunkPaths[index]))));
        }

        var expectedOccurrences = payloads.Sum(payload => TfragTerrainReader.Read(payload.Bytes).Chunks.Count);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var extraction = TfragPieceConverter.Extract(payloads, GameId.UYA);
        timer.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Expect(extraction.Occurrences.Count == expectedOccurrences,
            "largest-level extraction should emit one occurrence per native group");
        Expect(extraction.Assets.Count > 0 && extraction.Assets.Count <= extraction.Occurrences.Count,
            "largest-level extraction should emit a bounded canonical asset set");
        Expect(extraction.Assets.All(asset =>
                TfragPieceCanonicalCodec.ComputeContentId(asset.CanonicalBytes) == asset.ContentId),
            "largest-level canonical assets should re-hash to their reported IDs");

        Console.WriteLine(
            $"UYA level04 tfrag extraction profile: payloads={payloads.Count}, " +
            $"occurrences={extraction.Occurrences.Count}, assets={extraction.Assets.Count}, " +
            $"canonical-bytes={extraction.Assets.Sum(asset => (long)asset.CanonicalBytes.Length)}, " +
            $"allocated-bytes={allocatedBytes}, elapsed-ms={timer.ElapsedMilliseconds}.");
    }

    private static InvalidDataException CaptureInvalidData(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected invalid tfrag data to be rejected.");
    }

    private static void ExpectInvalidData(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void ExpectCanceled(Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException("Canceled tfrag extraction should not return partial output.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
