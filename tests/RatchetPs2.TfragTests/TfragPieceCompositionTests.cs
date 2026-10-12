using System.Buffers.Binary;
using System.Diagnostics;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.UYA.Builders;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Sdk;

internal static class TfragPieceCompositionTests
{
    public static void Run(string repoRoot)
    {
        ValidateTriangleWindingComparison();
        ValidateSyntheticComposition();
        ValidateLocalComposition(repoRoot);
        ValidateLevel09ChunkComposition(repoRoot);
    }

    private static void ValidateTriangleWindingComparison()
    {
        var vertices = new[]
        {
            Vertex(0, 0, 0),
            Vertex(1, 0, 0),
            Vertex(0, 1, 0),
        };
        var expected = new TfragPiecePrimitive(new(0, false, false), vertices, [0, 1, 2]);
        var cyclic = expected with { Indices = [1, 2, 0] };
        var reversed = expected with { Indices = [0, 2, 1] };
        Expect(UyaTfragTerrainComposer.TrianglesEquivalent(expected, cyclic),
            "semantic validation should accept cyclic triangle ordering");
        Expect(!UyaTfragTerrainComposer.TrianglesEquivalent(expected, reversed),
            "semantic validation should reject reversed triangle winding");
    }

    private static TfragPieceVertex Vertex(float x, float y, float z) => new(
        new(x, y, z),
        new(0, 0, 1),
        new(0, 0),
        new(1, 1, 1, 1),
        0,
        new(1, 1, 1, 1),
        new(0, 0, 1),
        1);

    private static void ValidateSyntheticComposition()
    {
        var source = CreateCompositionTerrain();
        var extraction = TfragPieceConverter.Extract(
            [new("level_wad/terrain.bin", TfragPieceSourceKind.Primary, 0, source)],
            GameId.UYA);
        var canonical = extraction.Assets.Single().CanonicalBytes;
        var noEdit = TfragPieceConverter.Compose(source, GameId.UYA, []);
        Expect(noEdit.ChangedPieceCount == 0 && noEdit.Bytes.SequenceEqual(source),
            "no-edit tfrag composition should preserve every source byte");
        Expect(ReferenceEquals(source, noEdit.Bytes),
            "no-edit tfrag composition should return the source buffer without copying it");
        var unchanged = TfragPieceConverter.Compose(source, GameId.UYA,
            [new("piece-0", 0, canonical, extraction.Occurrences[0].Transform)]);
        Expect(unchanged.ChangedPieceCount == 0 && ReferenceEquals(source, unchanged.Bytes),
            "identity tfrag edits should return the source buffer without copying it");

        var moved = new TfragPieceCompositionEdit(
            "piece-0",
            0,
            canonical,
            extraction.Occurrences[0].Transform with
            {
                Position = new(2, 2, 3),
                Scale = new(0.5f, 0.5f, 0.5f),
            });
        var first = TfragPieceConverter.Compose(source, GameId.UYA, [moved]);
        var second = TfragPieceConverter.Compose(source, GameId.UYA, [moved]);
        var terrain = TfragTerrainReader.Read(first.Bytes);
        Expect(first.ChangedPieceCount == 1, "one edited tfrag should report one changed piece");
        Expect(terrain.Chunks[0].BoundingSphere.X == 2048,
            "edited tfrag translation should update the native group center");
        Expect(first.Bytes.AsSpan(0x80, 0x40).SequenceEqual(source.AsSpan(0x80, 0x40))
            && first.Bytes.AsSpan(0x110).SequenceEqual(source.AsSpan(0x110)),
            "editing one tfrag should leave unrelated records and group data byte-identical");
        Expect(BinaryPrimitives.ReadUInt16LittleEndian(first.Bytes.AsSpan(0xCC, 2)) == 500,
            "tfrag composition should scale the mesh-sphere radius at +0x0c");
        Expect(BinaryPrimitives.ReadUInt16LittleEndian(first.Bytes.AsSpan(0xCE, 2)) == 0x44FF,
            "tfrag composition should preserve packed mesh-sphere metadata at +0x0e");
        Expect(BinaryPrimitives.ReadInt16LittleEndian(first.Bytes.AsSpan(0xD0, 2)) == 27
            && BinaryPrimitives.ReadInt16LittleEndian(first.Bytes.AsSpan(0xD2, 2)) == 27
            && BinaryPrimitives.ReadInt16LittleEndian(first.Bytes.AsSpan(0xD4, 2)) == 43,
            "tfrag composition should transform the quantized world-space culling cube");
        Expect(first.Bytes.SequenceEqual(second.Bytes),
            "repeated tfrag composition should be deterministic");

        var corrupt = canonical.ToArray();
        corrupt[^1] ^= 1;
        ExpectInvalid(() => TfragPieceConverter.Compose(
            source, GameId.UYA, [moved with { CanonicalBytes = corrupt }]),
            "composition should reject a canonical asset that does not match its source group");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ExpectCanceled(() => TfragPieceConverter.Compose(source, GameId.UYA, [moved], cancellation.Token));
    }

    private static void ValidateLocalComposition(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "test-assets", "tfrags", "UYA", "level5", "terrain", "terrain.bin");
        if (!File.Exists(path)) return;
        var source = File.ReadAllBytes(path);
        var extraction = TfragPieceConverter.Extract(
            [new("level_wad/terrain.bin", TfragPieceSourceKind.Primary, 0, source)],
            GameId.UYA);
        var occurrence = extraction.Occurrences[0];
        var asset = extraction.Assets.Single(value => value.ContentId == occurrence.ContentId);
        var angle = MathF.PI / 12;
        var edit = new TfragPieceCompositionEdit(
            "local-piece-0",
            occurrence.Provenance.GroupIndex,
            asset.CanonicalBytes,
            occurrence.Transform with
            {
                Position = occurrence.Transform.Position with { X = occurrence.Transform.Position.X + 1 },
                Rotation = new(0, 0, MathF.Sin(angle / 2), MathF.Cos(angle / 2)),
                Scale = new(1.05f, 1.05f, 1.05f),
            });

        var composition = TfragPieceConverter.Compose(source, GameId.UYA, [edit]);

        Expect(composition.ChangedPieceCount == 1 && composition.Bytes.Length == source.Length,
            "local tfrag composition should preserve payload layout while editing one native group");
        var sourceTerrain = TfragTerrainReader.Read(source);
        var sourceChunk = sourceTerrain.Chunks[occurrence.Provenance.GroupIndex];
        if (sourceChunk.MSphereCount > 0)
        {
            var meshSphere = sourceChunk.DataOffset + sourceChunk.MSphereOffset;
            var sourceRadius = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(meshSphere + 12, 2));
            var expectedRadius = (ushort)MathF.Round(sourceRadius * edit.Transform.Scale.X, MidpointRounding.ToEven);
            Expect(BinaryPrimitives.ReadUInt16LittleEndian(
                    composition.Bytes.AsSpan(meshSphere + 12, 2)) == expectedRadius,
                "tfrag composition should scale the native mesh-sphere radius");
            Expect(composition.Bytes.AsSpan(meshSphere + 14, 2).SequenceEqual(source.AsSpan(meshSphere + 14, 2)),
                "tfrag composition should preserve packed mesh-sphere LOD metadata");
        }
    }

    private static void ValidateLevel09ChunkComposition(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "test-assets", "extractions_uya", "level09.wad");
        if (!File.Exists(path)) return;
        var wadBytes = File.ReadAllBytes(path);
        var level = UyaLevelWadReader.ReadLevelWad(wadBytes);
        var chunkWad = UyaLevelWadReader.ReadSectorFileBlock(wadBytes, level.Chunks[2]);
        var source = TfragChunkWadReader.ReadTerrainPayload(chunkWad);
        var extraction = TfragPieceConverter.Extract(
            [new("level_wad/chunks/chunk2.wad", TfragPieceSourceKind.Chunk, 2, source)],
            GameId.UYA);

        TfragPieceCompositionEdit Edit(int groupIndex, TfragPieceTransform transform)
        {
            var occurrence = extraction.Occurrences.Single(value => value.Provenance.GroupIndex == groupIndex);
            var asset = extraction.Assets.Single(value => value.ContentId == occurrence.ContentId);
            return new($"level09-chunk2-group{groupIndex}", groupIndex, asset.CanonicalBytes, transform);
        }

        var group2 = extraction.Occurrences.Single(value => value.Provenance.GroupIndex == 2);
        var group53 = extraction.Occurrences.Single(value => value.Provenance.GroupIndex == 53);
        var group53Transform = group53.Transform with
        {
            Position = new(776.923f, 530.0021f, 109.27637f),
            Rotation = new(-0.7216277f, -0.09849136f, -0.104960166f, 0.67715305f),
        };
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var composition = TfragPieceConverter.Compose(source, GameId.UYA,
        [
            Edit(2, group2.Transform with
            {
                Position = new(775.60126f, 589.99164f, 105.658516f),
                Rotation = new(0.46299678f, -0.41033548f, 0.5210988f, 0.5879752f),
                Scale = new(0.29524815f, 0.29524815f, 0.29524815f),
            }),
            Edit(53, group53Transform),
        ]);
        timer.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Expect(composition.ChangedPieceCount == 2 && composition.Bytes.Length == source.Length,
            "level09 chunk2 should compose small rotated and enlarged pieces together");

        var group53Asset = extraction.Assets.Single(value => value.ContentId == group53.ContentId);
        var candidate = CollisionWork.GenerateTfragSurfaceCandidate(
            group53Asset.CanonicalBytes, GameId.UYA, "level09-chunk2-group53", lodIndex: 0);
        var sourceTriangleCount = group53Asset.Geometry.Lods.Single(lod => lod.LodIndex == 0)
            .Primitives.Sum(primitive => primitive.Indices.Count / 3);
        Expect(candidate.GeneratedFaceCount == sourceTriangleCount
            && candidate.MergedQuadCount == 0
            && candidate.Addition.Faces.All(face => !face.IsQuad),
            "level09 chunk2 group53 collision should retain every authored high-LOD triangle");
        var decimated75 = CollisionWork.GenerateTfragDecimatedCandidate(
            group53Asset.CanonicalBytes, GameId.UYA, "level09-chunk2-group53-75", targetFaceRatio: 0.75f);
        var decimated50 = CollisionWork.GenerateTfragDecimatedCandidate(
            group53Asset.CanonicalBytes, GameId.UYA, "level09-chunk2-group53-50", targetFaceRatio: 0.5f);
        Expect(decimated75.GeneratedFaceCount < candidate.GeneratedFaceCount
            && decimated50.GeneratedFaceCount <= decimated75.GeneratedFaceCount
            && decimated50.Addition.Faces.All(face => !face.IsQuad),
            "level09 chunk2 group53 decimation should progressively reduce triangle collision");
        var addition = CollisionWork.TransformAddition(
            candidate.Addition,
            GameId.UYA,
            candidate.Addition.Id,
            ToCollisionTransform(group53Transform));
        var sourceCollision = TfragChunkWadReader.ReadCollisionPayload(chunkWad);
        var collision = CollisionWork.Compose(sourceCollision, GameId.UYA, [], [addition]);
        var affectedCollisionOctants = collision.Analysis?.Octants.Count(value => value.AdditionIds.Count > 0)
            ?? throw new InvalidOperationException("level09 collision composition did not return analysis");
        var sourceBarrierOffset = BinaryPrimitives.ReadInt32LittleEndian(sourceCollision.AsSpan(4));
        var sourceMeshOffset = BinaryPrimitives.ReadInt32LittleEndian(sourceCollision);
        var outputBarrierOffset = BinaryPrimitives.ReadInt32LittleEndian(collision.Bytes.AsSpan(4));
        var outputMeshOffset = BinaryPrimitives.ReadInt32LittleEndian(collision.Bytes);
        var preservedMeshDifferenceCount = CountDifferences(
            sourceCollision.AsSpan(sourceMeshOffset, sourceBarrierOffset - sourceMeshOffset),
            collision.Bytes.AsSpan(outputMeshOffset, sourceBarrierOffset - sourceMeshOffset));
        var composedChunk = LevelAssetComposer.ComposeTfragChunk(GameId.UYA, chunkWad, composition.Bytes);
        composedChunk = LevelAssetComposer.ComposeTfragChunkCollision(
            GameId.UYA, composedChunk, collision.Bytes);
        Expect(TfragChunkWadReader.ReadTerrainPayload(composedChunk).SequenceEqual(composition.Bytes)
            && TfragChunkWadReader.ReadCollisionPayload(composedChunk).SequenceEqual(collision.Bytes)
            && collision.Bytes.AsSpan(outputBarrierOffset)
                .StartsWith(sourceCollision.AsSpan(sourceBarrierOffset))
            && outputMeshOffset == sourceMeshOffset
            && outputBarrierOffset > sourceBarrierOffset
            && preservedMeshDifferenceCount > 0
            && preservedMeshDifferenceCount <= affectedCollisionOctants * sizeof(int)
            && collision.Bytes.Length <= sourceCollision.Length + 0x10000,
            "level09 chunk2 should patch native lookups and relocate barriers after the enlarged mesh");

        var sourceInventory = UyaLevelWadInventoryReader.Read(wadBytes);
        var sourceChunkBank = sourceInventory.Containers.Single(value => value.Path == "level_wad")
            .Slots.Single(value => value.Path == "level_wad/chunks/chunk2_bank.wad").Bytes;
        var archive = LevelArchiveBuilder.Build(GameId.UYA, wadBytes,
            new Dictionary<string, ReadOnlyMemory<byte>>
            {
                ["level_wad/chunks/chunk2.wad"] = composedChunk,
            });
        Expect(archive.Succeeded && archive.OutputBytes is not null,
            "level09 archive should accept combined streamed terrain and collision edits");
        var outputInventory = UyaLevelWadInventoryReader.Read(archive.OutputBytes!);
        var outputRoot = outputInventory.Containers.Single(value => value.Path == "level_wad");
        var outputChunkSlot = outputRoot.Slots.Single(value => value.Path == "level_wad/chunks/chunk2.wad");
        var outputChunkBankSlot = outputRoot.Slots.Single(value => value.Path == "level_wad/chunks/chunk2_bank.wad");
        var outputLevel = UyaLevelWadReader.ReadLevelWad(archive.OutputBytes!);
        Expect(TfragChunkWadReader.ReadTerrainPayload(outputChunkSlot.Bytes.Span).SequenceEqual(composition.Bytes)
            && TfragChunkWadReader.ReadCollisionPayload(outputChunkSlot.Bytes.Span).SequenceEqual(collision.Bytes)
            && outputChunkBankSlot.Bytes.Span.SequenceEqual(sourceChunkBank.Span),
            "level09 archive packing should preserve edited terrain, generated collision, and its following chunk bank");
        Expect(outputLevel.Chunks[2].Length
                == (composedChunk.Length + UyaLevelConstants.SectorSize - 1) / UyaLevelConstants.SectorSize
            && outputLevel.Chunks[2].Offset * UyaLevelConstants.SectorSize == outputChunkSlot.Offset
            && outputLevel.ChunkBanks[2].Offset * UyaLevelConstants.SectorSize == outputChunkBankSlot.Offset
            && outputLevel.ChunkBanks[2].Length * UyaLevelConstants.SectorSize == outputChunkBankSlot.Length,
            "level09 packing should rewrite the chunk and following bank declarations to their aligned byte ranges");
        Console.WriteLine(
            $"UYA level09 chunk2 composition profile: groups={extraction.Occurrences.Count}, edits=2, "
            + $"payload-bytes={source.Length}, allocated-bytes={allocatedBytes}, elapsed-ms={timer.ElapsedMilliseconds}, "
            + $"collision-faces={candidate.GeneratedFaceCount}, collision-octants={candidate.Analysis.OccupiedOctantCount}, "
            + $"collision-bytes={sourceCollision.Length}->{collision.Bytes.Length}, "
            + $"chunk-bytes={chunkWad.Length}->{composedChunk.Length}.");
    }

    private static CollisionInstanceTransform ToCollisionTransform(TfragPieceTransform transform) => new(
        new(transform.Position.X, transform.Position.Y, transform.Position.Z),
        new(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W),
        new(transform.Scale.X, transform.Scale.Y, transform.Scale.Z));

    private static int CountDifferences(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) throw new ArgumentException("Compared spans must have equal lengths.");
        var count = 0;
        for (var index = 0; index < left.Length; index++)
            if (left[index] != right[index]) count++;
        return count;
    }

    private static byte[] CreateCompositionTerrain()
    {
        const int tableOffset = 0x40;
        const int recordSize = 0x40;
        const int dataSize = 0x50;
        var bytes = new byte[tableOffset + recordSize * 2 + dataSize * 2];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x00, 4), tableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x04, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x08, 4), BitConverter.SingleToInt32Bits(32f));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x0C, 4), 2);
        for (var index = 0; index < 2; index++)
        {
            var record = tableOffset + index * recordSize;
            var data = tableOffset + recordSize * 2 + index * dataSize;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record, 4), BitConverter.SingleToInt32Bits(6400));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 4, 4), BitConverter.SingleToInt32Bits(12800));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 8, 4), BitConverter.SingleToInt32Bits(19200));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 12, 4), BitConverter.SingleToInt32Bits(1000));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 0x10, 4), data - tableOffset);
            bytes[record + 0x2C] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(record + 0x2E, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(record + 0x30, 2), -1);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(record + 0x38, 2), 0x10);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(data, 4), BitConverter.SingleToInt32Bits(6400));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(data + 4, 4), BitConverter.SingleToInt32Bits(12800));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(data + 8, 4), BitConverter.SingleToInt32Bits(19200));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(data + 12, 2), 1000);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(data + 14, 2), 0x44FF);
            for (var cube = 0; cube < 8; cube++)
            {
                var vertex = data + 0x10 + cube * 8;
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(vertex, 2), (short)((cube & 1) == 0 ? 90 : 110));
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(vertex + 2, 2), (short)((cube & 2) == 0 ? 190 : 210));
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(vertex + 4, 2), (short)((cube & 4) == 0 ? 290 : 310));
            }
        }
        return bytes;
    }

    private static void ExpectInvalid(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void ExpectCanceled(Action action)
    {
        try { action(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Canceled tfrag composition should not return partial output.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
