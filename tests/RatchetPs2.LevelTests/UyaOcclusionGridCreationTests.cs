using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Games.UYA.Occlusion;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Sdk;

internal static class UyaOcclusionGridCreationTests
{
    internal static void Run()
    {
        VerifyEmptyTemplate();
        VerifyGoldenCell();
        VerifyCoverageChanges();
        VerifyLimits();
        VerifyLevelComposition();
        Console.WriteLine("PASS source-free occlusion grids, explicit reserved bits, deterministic readback and preflight limits");
    }

    private static void VerifyEmptyTemplate()
    {
        var bytes = OcclusionComposer.CreateEmptyGrid(GameId.UYA);
        var grid = UyaOcclusionGridReader.Read(bytes);
        Check(bytes.Length == 64 && grid.MasksOffset == 16 && grid.Octants.Count == 0,
            "Disabled occlusion must have a valid aligned header with no cells or masks.");
        Check(bytes.AsSpan(4).IndexOfAnyExcept((byte)0) < 0, "Empty templates contain no retained visibility data.");
        Throws<OperationCanceledException>(() => OcclusionComposer.CreateEmptyGrid(GameId.UYA, new(true)));
        Throws<NotSupportedException>(() => OcclusionComposer.CreateEmptyGrid(GameId.DL));
    }

    private static void VerifyGoldenCell()
    {
        var mask = new byte[128]; mask[0] = 1; mask[127] = 128;
        var masks = new Dictionary<(int, int, int), byte[]> { [(1, 2, 3)] = mask };
        var output = OcclusionComposer.CreateGrid(GameId.UYA, masks);
        var expected = new byte[192];
        BinaryPrimitives.WriteInt32LittleEndian(expected, 32);
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(at), value);
        U16(4, 3); U16(6, 1); U16(8, 3); U16(12, 2); U16(14, 1); U16(16, 5); U16(20, 1); U16(22, 1);
        expected[32] = 1; expected[32 + 127] = 128;
        Check(output.SequenceEqual(expected), "Independent single-cell layout, mask bytes and padding must match.");
        Check(OcclusionCoverageBuilder.ReadCoordinates(GameId.UYA, output).SequenceEqual(new[]
            { new OcclusionCoverageCoordinate(1, 2, 3) }), "Neutral coverage reading must preserve native coordinates.");
        Throws<OperationCanceledException>(() => OcclusionCoverageBuilder.ReadCoordinates(GameId.UYA, output, new(true)));
        Throws<NotSupportedException>(() => OcclusionCoverageBuilder.ReadCoordinates(GameId.DL, output));
        Throws<InvalidDataException>(() => OcclusionCoverageBuilder.ReadCoordinates(GameId.UYA, new byte[4]));
        Check(output.SequenceEqual(OcclusionComposer.ReplaceMasks(GameId.UYA, expected, masks)),
            "Source-free creation and source mask replacement must share one encoding.");
        mask.AsSpan().Fill(255);
        Check(output.SequenceEqual(expected), "The output must not retain caller mask storage.");
    }

    private static void VerifyCoverageChanges()
    {
        var a = new byte[128]; a[0] = 1;
        var b = new byte[128]; b[0] = 2; b[127] = 128;
        var masks = new Dictionary<(int, int, int), byte[]>
        {
            [(12, 20, 30)] = b, [(10, 22, 32)] = a, [(10, 20, 30)] = a,
        };
        var output = OcclusionComposer.CreateGrid(GameId.UYA, masks);
        VerifyMasks(output, masks);
        var grid = UyaOcclusionGridReader.Read(output);
        Check(grid.Octants.Select(c => (c.X, c.Y, c.Z)).SequenceEqual(new[]
            { (10, 20, 30), (12, 20, 30), (10, 22, 32) }), "Sparse coverage must have canonical Z/Y/X order.");
        Check(grid.Octants.Select(c => c.MaskIndex).Distinct().Count() == 2, "Identical complete masks must share a slot.");
        Check(grid.MasksOffset % 16 == 0 && output.Length % 64 == 0, "Native table and payload alignment must hold.");
        Check(output.SequenceEqual(OcclusionComposer.CreateGrid(GameId.UYA,
            masks.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value))), "Dictionary order must not affect bytes.");

        masks.Remove((10, 20, 30));
        masks.Add((9, 19, 29), b);
        masks.Add((11, 21, 31), a);
        var changed = OcclusionComposer.CreateGrid(GameId.UYA, masks);
        VerifyMasks(changed, masks);
        Check(!changed.SequenceEqual(output), "Added/removed/moved coordinates must be reflected without a source grid.");
        Check(a[0] == 1 && a[127] == 0 && b[0] == 2 && b[127] == 128, "Serialization must leave masks unchanged.");

        var maximum = new Dictionary<(int, int, int), byte[]> { [(131069, 65535, 65536)] = a };
        VerifyMasks(OcclusionComposer.CreateGrid(GameId.UYA, maximum), maximum);
    }

    private static void VerifyLimits()
    {
        var mask = new byte[128];
        var one = new Dictionary<(int, int, int), byte[]> { [(0, 0, 0)] = mask };
        Throws<ArgumentNullException>(() => OcclusionComposer.CreateGrid(GameId.UYA, null!));
        Throws<InvalidDataException>(() => OcclusionComposer.CreateGrid(GameId.UYA, new Dictionary<(int, int, int), byte[]>()));
        foreach (var coordinate in new[] { (-1, 0, 0), (0, -1, 0), (0, 0, -1), (int.MaxValue, 0, 0), (131070, 0, 0) })
            Throws<InvalidDataException>(() => OcclusionComposer.CreateGrid(GameId.UYA,
                new Dictionary<(int, int, int), byte[]> { [coordinate] = mask }));
        foreach (var coordinate in new[] { (65535, 0, 0), (0, 65535, 0), (0, 0, 65535) })
            Throws<InvalidDataException>(() => OcclusionComposer.CreateGrid(GameId.UYA,
                new Dictionary<(int, int, int), byte[]> { [(0, 0, 0)] = mask, [coordinate] = mask }));
        foreach (var invalid in new[] { null, new byte[127], new byte[129] })
            Throws<InvalidDataException>(() => OcclusionComposer.CreateGrid(GameId.UYA,
                new Dictionary<(int, int, int), byte[]> { [(0, 0, 0)] = invalid! }));
        Throws<OperationCanceledException>(() => OcclusionComposer.CreateGrid(GameId.UYA, one, new(true)));
        Throws<NotSupportedException>(() => OcclusionComposer.CreateGrid(GameId.DL, one));

        var tooMany = Enumerable.Range(0, OcclusionSamplePlanner.MaximumCells + 1)
            .ToDictionary(i => (i % 100, i / 100 % 100, i / 10000), _ => mask);
        Throws<InvalidDataException>(() => OcclusionComposer.CreateGrid(GameId.UYA, tooMany));
        var tooSparse = Enumerable.Range(0, 20_000).ToDictionary(i => (0, 0, i), _ => mask);
        var pointerError = Throws<InvalidDataException>(() => OcclusionComposer.CreateGrid(GameId.UYA, tooSparse));
        Check(pointerError.Message.Contains("pointer capacity", StringComparison.Ordinal), "Preflight must enforce row-pointer limits.");
        var tooManyMasks = Enumerable.Range(0, 65_536).ToDictionary(i => (i % 256, i / 256, 0), i =>
        {
            var bytes = new byte[128]; BinaryPrimitives.WriteInt32LittleEndian(bytes, i); return bytes;
        });
        var maskError = Throws<InvalidDataException>(() => OcclusionComposer.CreateGrid(GameId.UYA, tooManyMasks));
        Check(maskError.Message.Contains("slot capacity", StringComparison.Ordinal), "The missing-cell sentinel must never become a mask slot.");
    }

    private static void VerifyLevelComposition()
    {
        var sourceMask = new byte[128]; sourceMask[0] = 7;
        var original = OcclusionComposer.CreateGrid(GameId.UYA,
            new Dictionary<(int, int, int), byte[]> { [(1, 2, 3)] = sourceMask });
        var header = new byte[0xc0];
        var adjacent = 16 + original.Length;
        var assets = new byte[adjacent + 32];
        assets.AsSpan(0, 16).Fill(0xa1); original.CopyTo(assets, 16); assets.AsSpan(adjacent).Fill(0xd3);
        void I32(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(at), value);
        I32(0x0c, 16); I32(0x7c, assets.Length); I32(0x8c, assets.Length); I32(0xa4, adjacent);
        var level = LevelFixture(header, assets); var before = level.ToArray();
        var masks = Enumerable.Range(0, 30).ToDictionary(i => (30 + i, 2, 3), i =>
        {
            var bytes = new byte[128]; bytes[0] = (byte)(i + 1); bytes[127] = (byte)(i % 2 == 0 ? 0 : 128); return bytes;
        });
        var maskCopies = masks.ToDictionary(p => p.Key, p => p.Value.ToArray());
        var result = OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, level, masks);
        Check(result.Succeeded && result.OutputBytes is not null, "Arbitrary coverage must compose into a complete level.");
        var output = result.OutputBytes!;
        var native = OcclusionComposer.CreateGrid(GameId.UYA, masks);
        Check(output.SequenceEqual(OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, level, native, masks.Count).OutputBytes!),
            "Cached generated grids must reuse the same validated application path.");
        Throws<InvalidDataException>(() => OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, level, native, masks.Count + 1));
        Throws<InvalidDataException>(() => OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, level, new byte[4], 1));
        Throws<OperationCanceledException>(() => OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, level, native, masks.Count, new(true)));
        VerifyMasks(OcclusionComposer.ReadGrid(GameId.UYA, output), masks);
        Check(output.SequenceEqual(OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, level,
            masks.Reverse().ToDictionary(p => p.Key, p => p.Value)).OutputBytes!), "Complete-level output must be deterministic.");
        var cleared = OcclusionComposer.ClearCoverageFromLevel(GameId.UYA, output);
        Check(cleared.Succeeded && OcclusionComposer.ReadGrid(GameId.UYA, cleared.OutputBytes!)
            .SequenceEqual(OcclusionComposer.CreateEmptyGrid(GameId.UYA)), "Clearing coverage must pack an exact empty native template.");
        var clearedFiles = UyaLevelWadUnpacker.Unpack(cleared.OutputBytes!).Files;
        foreach (var file in UyaLevelWadUnpacker.Unpack(output).Files.Where(f => f.Path is not
            ("level_wad/header.bin" or "level_wad/level_data.wad" or "level_data/header.bin" or "assets/asset_header.bin" or "assets/asset_wad.bin")))
            Check(clearedFiles.Single(f => f.Path == file.Path).Bytes.SequenceEqual(file.Bytes), "Clearing coverage preserves auxiliary level payloads.");
        Throws<OperationCanceledException>(() => OcclusionComposer.ClearCoverageFromLevel(GameId.UYA, output, new(true)));
        var oldFiles = UyaLevelWadUnpacker.Unpack(level).Files;
        var newFiles = UyaLevelWadUnpacker.Unpack(output).Files;
        foreach (var file in oldFiles.Where(f => f.Path is not ("level_wad/header.bin" or "level_wad/level_data.wad" or "level_data/header.bin" or "assets/asset_header.bin" or "assets/asset_wad.bin")))
            Check(newFiles.Single(f => f.Path == file.Path).Bytes.SequenceEqual(file.Bytes), "Auxiliary/opaque level payloads must survive grid replacement.");
        var grown = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(newFiles);
        var moved = BinaryPrimitives.ReadInt32LittleEndian(grown.HeaderBytes.AsSpan(0xa4));
        Check(moved > adjacent && grown.AssetWadBytes.AsSpan(moved, 32).SequenceEqual(assets.AsSpan(adjacent)),
            "Growth must relocate and preserve the adjacent opaque asset.");
        Check(grown.AssetWadBytes.AsSpan(0, 16).SequenceEqual(assets.AsSpan(0, 16)), "Growth must preserve opaque prefix bytes.");
        var shrunk = OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, output,
            new Dictionary<(int, int, int), byte[]> { [(1, 2, 3)] = sourceMask }).OutputBytes!;
        VerifyMasks(OcclusionComposer.ReadGrid(GameId.UYA, shrunk), new Dictionary<(int, int, int), byte[]> { [(1, 2, 3)] = sourceMask });
        var restored = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(UyaLevelWadUnpacker.Unpack(shrunk).Files);
        var expectedHeader = header.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(expectedHeader.AsSpan(0x88), assets.Length);
        Check(restored.HeaderBytes.SequenceEqual(expectedHeader) && restored.AssetWadBytes.SequenceEqual(assets),
            "Shrinking back must restore every asset byte and pointer; archive packing regenerates its uncompressed-size field.");
        Throws<InvalidDataException>(() => OcclusionComposer.ApplyToLevel(GameId.UYA, level,
            OcclusionComposer.CreateGrid(GameId.UYA, masks), masks.Count));
        Throws<OperationCanceledException>(() => OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, level, masks, new(true)));
        Throws<NotSupportedException>(() => OcclusionComposer.ApplyCoverageToLevel(GameId.DL, level, masks));
        Throws<InvalidDataException>(() => OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, level, new Dictionary<(int, int, int), byte[]>()));
        Throws<InvalidDataException>(() => OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, level,
            new Dictionary<(int, int, int), byte[]> { [(0, 0, 0)] = new byte[127] }));
        Check(level.SequenceEqual(before) && masks.All(p => p.Value.SequenceEqual(maskCopies[p.Key])), "Composition/rejection/cancellation must preserve caller inputs.");

        // A missing grid is inserted into the scene view, ahead of assets loaded outside that view.
        Array.Resize(ref header, 0x4c0);
        I32(0x0c, 0); I32(0x7c, adjacent); I32(0x78, 0xc0); I32(0xc0, adjacent);
        var withoutGrid = LevelFixture(header, assets);
        var inserted = OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, withoutGrid, masks).OutputBytes!;
        VerifyMasks(OcclusionComposer.ReadGrid(GameId.UYA, inserted), masks);
        var added = UyaLevelWadRenderPackageBuilder.ReadAssetSourceFiles(UyaLevelWadUnpacker.Unpack(inserted).Files);
        var gridOffset = BinaryPrimitives.ReadInt32LittleEndian(added.HeaderBytes.AsSpan(0x0c));
        var boundary = BinaryPrimitives.ReadInt32LittleEndian(added.HeaderBytes.AsSpan(0x7c));
        var newAdjacent = BinaryPrimitives.ReadInt32LittleEndian(added.HeaderBytes.AsSpan(0xa4));
        Check(gridOffset == adjacent && boundary == newAdjacent && newAdjacent > adjacent,
            "A source-free grid must stay inside the scene view and relocate its following assets.");
        Check(added.AssetWadBytes.AsSpan(newAdjacent, 32).SequenceEqual(assets.AsSpan(adjacent)), "Insertion must preserve following assets.");
        Check(BinaryPrimitives.ReadInt32LittleEndian(added.HeaderBytes.AsSpan(0xc0)) == newAdjacent,
            "Insertion must relocate the trailing animation sequence table.");
        I32(0x7c, assets.Length);
        VerifyMasks(OcclusionComposer.ReadGrid(GameId.UYA,
            OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, LevelFixture(header, assets), masks).OutputBytes!), masks);
        I32(0x7c, 0);
        Throws<InvalidDataException>(() => OcclusionComposer.ApplyCoverageToLevel(GameId.UYA, LevelFixture(header, assets), masks));
    }

    private static byte[] LevelFixture(byte[] header, byte[] assets)
    {
        const int sector = UyaLevelConstants.SectorSize;
        var bytes = new byte[4 * sector];
        void I32(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
        I32(0, UyaLevelConstants.LevelWadHeaderSize); I32(0x10, 1); I32(0x14, 1);
        I32(0x18, 2); I32(0x1c, 1); I32(0x28, 3); I32(0x2c, 1);
        I32(sector + 0x08, 0x80); I32(sector + 0x0c, header.Length);
        var paletteOffset = 0x80 + header.Length;
        var assetOffset = paletteOffset + 16;
        I32(sector + 0x10, paletteOffset); I32(sector + 0x14, 16);
        I32(sector + 0x48, assetOffset); I32(sector + 0x4c, assets.Length);
        header.CopyTo(bytes, sector + 0x80); bytes.AsSpan(sector + paletteOffset, 16).Fill(0xa5);
        assets.CopyTo(bytes, sector + assetOffset); bytes.AsSpan(2 * sector, sector).Fill(0x53); bytes.AsSpan(3 * sector, sector).Fill(0x6f);
        return bytes;
    }

    private static void VerifyMasks(byte[] output, IReadOnlyDictionary<(int, int, int), byte[]> expected)
    {
        var grid = UyaOcclusionGridReader.Read(output);
        Check(grid.Octants.Count == expected.Count, "Readback must contain exactly the supplied cells.");
        foreach (var cell in grid.Octants)
            Check(expected.TryGetValue((cell.X, cell.Y, cell.Z), out var mask)
                && output.AsSpan(grid.MasksOffset + cell.MaskIndex * 128, 128).SequenceEqual(mask),
                "Every coordinate and complete explicit mask must survive readback.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T exception) { return exception; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
