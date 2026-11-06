using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Skyboxes;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.DL.Gameplay;
using RatchetPs2.Games.GC.Skyboxes;
using RatchetPs2.Games.RC1.Gameplay;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Sdk;

internal static class LevelContentConversionTests
{
    public static void Run()
    {
        VerifySplitInstances();
        VerifyDl(); VerifyRc1(); VerifyGc(); VerifyWriters(); VerifyTextureRepair();
        VerifyGcRawInventory(Fixture("gc-content")); VerifyStreamedInventory(Fixture("gc-content"));
        VerifySparseTieMappings(Fixture("gc-content")); VerifyAppendedMappings();
        Console.WriteLine("Level-content conversion: native instances, textures, sky, lighting, mappings, metadata, repair and cancellation passed.");
    }

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LevelContent", name + ".wad"));

    private static void VerifySplitInstances()
    {
        var conversion = LevelContentConverter.Convert(GameId.RC1, GameId.UYA, Fixture("rc1-content"));
        var blocks = conversion.Files.Where(file => file.Path.StartsWith("gameplay/core/", StringComparison.Ordinal))
            .ToDictionary(file => Path.GetFileNameWithoutExtension(file.Path), file => file.Bytes);
        var sourceTie = UyaTieInstancesReader.Read(blocks["tie_instances"]).Instances.Single();
        var mapping = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(mapping.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(mapping.AsSpan(16), 3);
        BinaryPrimitives.WriteInt32LittleEndian(mapping.AsSpan(20), sourceTie.OcclusionId);
        blocks["occlusion"] = mapping;
        blocks["tie_groups"] = UyaTieGroupsWriter.Write([new[] { 0 }]);
        conversion = conversion with { Files = conversion.Files.Where(file => !file.Path.StartsWith("gameplay/", StringComparison.Ordinal))
            .Concat(UyaConvertedLevelPackageBuilder.Gameplay(blocks)).ToArray() };
        var original = conversion.Files.ToDictionary(file => file.Path, file => file.Bytes.ToArray());
        var split = new Dictionary<int, IReadOnlyList<int>> { [0x1234] = new[] { 0x1234, 0x9000, 0x9001 } };
        var expanded = LevelContentConverter.ExpandTieModels(GameId.UYA, conversion, split);
        var source = UyaGameplayBlockReader.ReadCore(original["gameplay/gameplay_core.bin"]);
        var target = UyaGameplayBlockReader.ReadCore(expanded.Files.Single(file => file.Path == "gameplay/gameplay_core.bin").Bytes);
        var ties = UyaTieInstancesReader.Read(expanded.Files.Single(file => file.Path == "gameplay/core/tie_instances.bin").Bytes);
        Check(ties.Instances.Select(tie => tie.ClassId).SequenceEqual(split[0x1234]), "split classes expand placements");
        Check(ties.Instances.All(tie => tie.RawBytes.AsSpan(4).SequenceEqual(ties.Instances[0].RawBytes.AsSpan(4))),
            "split instances retain exact transforms, lighting selectors and occlusion IDs");
        Check(target.Lighting.TieAmbientRgbas.Length == 3 && target.Lighting.TieAmbientRgbas.All(colors =>
            colors.SequenceEqual(source.Lighting.TieAmbientRgbas[0])), "split instances retain complete ambient lighting");
        var mappings = UyaOcclusionMappingsReader.Read(expanded.Files.Single(file => file.Path == "gameplay/core/occlusion.bin").Bytes);
        Check(mappings.Ties.Count == 3 && mappings.Ties.All(mapping => mapping == mappings.Ties[0]), "split pieces share visibility");
        var groups = UyaTieGroupsReader.Read(expanded.Files.Single(file => file.Path == "gameplay/core/tie_groups.bin").Bytes);
        Check(groups.Groups.Single().SequenceEqual(new[] { 0, 1, 2 }), "split pieces inherit native group membership");
        Check(conversion.Files.All(file => file.Bytes.SequenceEqual(original[file.Path])), "expansion preserves donor buffers");
        Reject<ArgumentException>(() => LevelContentConverter.ExpandTieModels(GameId.UYA, conversion,
            new Dictionary<int, IReadOnlyList<int>> { [0x1234] = new[] { 0x1234, 0x1234 } }));
        Reject<OperationCanceledException>(() => LevelContentConverter.ExpandTieModels(GameId.UYA, conversion, split, new CancellationToken(true)));
    }

    private static void VerifyDl()
    {
        var wad = Fixture("dl-content");
        var originalWad = wad.ToArray();
        var conversion = LevelContentConverter.Convert(GameId.DL, GameId.UYA, wad);
        var assetFiles = conversion.Files.ToDictionary(file => file.Path, file => file.Bytes);
        Check(BinaryPrimitives.ReadInt32LittleEndian(assetFiles["assets/asset_wad.bin"]) == 0x40, "FX conversion preserves resident terrain");
        var skyBytes = assetFiles["assets/asset_wad.bin"].AsSpan(0x1100, 0x560).ToArray();
        CheckSkyTextures(skyBytes);
        Check(wad.AsSpan().SequenceEqual(originalWad), "sky conversion preserves the donor WAD");
        var fx = FxTextureCatalog.Read(GameId.UYA, assetFiles["assets/asset_header.bin"], assetFiles["assets/asset_wad.bin"]);
        var pixels = PifReader.Read(fx.Entries[0].CanonicalTextureBytes).PixelData;
        Check(Enumerable.Range(0, 256).All(index => pixels[TextureConverter.RemapPixelIndexFromRac4(index, 16)] == (byte)index), "FX pixels use the FX data base before unswizzling");
        Check(PifReader.Read(fx.Entries[1].CanonicalTextureBytes).PixelData.SequenceEqual(Enumerable.Range(0, 64).Select(index => (byte)index)), "small FX images retain their linear pixels");
        var emptyConversion = LevelContentConverter.Convert(GameId.DL, GameId.UYA, Fixture("dl-empty-fixups"));
        Check(emptyConversion.Files.Single(file => file.Path == "gameplay/core/pvar_relative_pointers.bin").Bytes.Length == 0,
            "empty zero-padded fixup tables are not interpreted as references");
        var native = UyaMobyInstancesReader.Read(conversion.Files.Single(file => file.Path == "gameplay/core/moby_instances.bin").Bytes);
        Check(native.Instances[0] is { ClassId: 100, Uid: 42, Mission: -1, DrawDistance: 123, PvarIndex: 0, Scale: 2 }, "DL Moby fields convert without offset drift");
        var original = DlGameplayBlockReader.ReadCore(conversion.RawGameplay).Blocks.Single(block => block.SemanticName == "moby_instances").PayloadBytes;
        Check(BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(0x10)) == 0x70, "raw DL gameplay remains untouched");
        var inventory = LevelContentInventoryReader.ReadConverted(GameId.DL, GameId.UYA, conversion);
        Check(inventory.All(value => value.Diagnostic is null), string.Join(' ', inventory.Select(value => value.Diagnostic)));
        var compressedInventory = LevelContentInventoryReader.ReadConverted(GameId.DL, GameId.UYA, conversion with
        {
            Files = conversion.Files.Select(file => file.Path == "assets/asset_wad.bin"
                ? file with { Bytes = WadCompression.Compress(file.Bytes) } : file).ToArray(),
        });
        Check(compressedInventory.All(value => value.Diagnostic is null)
            && compressedInventory.Select(value => value.Items.Sum(item => item.ItemCount))
                .SequenceEqual(inventory.Select(value => value.Items.Sum(item => item.ItemCount))),
            "normalized inventory reads compressed and uncompressed asset buffers identically");
        Check(inventory.Single(value => value.Family == "mapGroups").Items.Sum(item => item.ItemCount) == 3, "all world and gameplay group families are inventoried");
        Check(conversion.HasSceneSettings, "DL provides complete scene settings");
        Reject<OperationCanceledException>(() => LevelContentConverter.Convert(GameId.DL, GameId.UYA, wad, new CancellationToken(true)));
    }

    private static void VerifyRc1()
    {
        var wad = Fixture("rc1-content");
        var original = wad.ToArray();
        var conversion = LevelContentConverter.Convert(GameId.RC1, GameId.UYA, wad);
        var terrainTexture = PifReader.Read(conversion.TerrainTextures!.Single());
        Check(terrainTexture.Header.MipLevels == 3 && terrainTexture.MipPixelData[0].All(value => value == 2)
            && terrainTexture.MipPixelData[1].All(value => value == 3), "RC1 terrain retains the authored half and quarter mips supported by UYA");
        var gameplay = UyaGameplayBlockReader.ReadCore(conversion.Files.Single(file => file.Path == "gameplay/gameplay_core.bin").Bytes);
        var raw = Rc1Gameplay.ReadCore(conversion.RawGameplay);
        var nativeMoby = UyaMobyInstancesReader.Read(conversion.Files.Single(file => file.Path == "gameplay/core/moby_instances.bin").Bytes);
        Check(nativeMoby.Instances[0] is { ClassId: 0x1234, Uid: 42, DrawDistance: 123, PvarIndex: 0, Mission: -1 }, "RC1 Moby records convert to native UYA fields");
        Check(raw.Blocks.Single(block => block.SemanticName == "moby_instances").PayloadBytes[0x10] == 0x78, "untouched RC1 instance records remain archived");
        var sourceColors = raw.Blocks.Single(block => block.SemanticName == "tie_instances").PayloadBytes.AsSpan(0x60, 0x80).ToArray();
        var nativeColors = gameplay.Lighting.TieAmbientRgbas[0];
        var expectedColors = sourceColors.ToArray();
        for (var offset = 1; offset < expectedColors.Length; offset += 2) expectedColors[offset] &= 0x7f;
        Check(nativeColors.Length == 0x84 && nativeColors.AsSpan(0, 4).SequenceEqual(new byte[4])
            && nativeColors.AsSpan(4).SequenceEqual(expectedColors),
            "RC1 RGB5 colors retain RGB and gain a zero-base, zero-shift UYA header with native alpha");
        Check((sourceColors[1] & 0x80) != 0 && sourceColors[3] < 0x80,
            "lighting fixture includes both packed alpha values");
        for (var slot = 0; slot < 64; slot++)
        {
            var packed = BinaryPrimitives.ReadUInt16LittleEndian(nativeColors.AsSpan(4 + slot * 2));
            var alpha = Math.Min(255, 128 + (packed >> 15) * 128);
            Check(alpha == 128, "UYA lighting expansion produces opaque alpha 128 instead of overblending at 255");
        }
        Check(wad.SequenceEqual(original) && conversion.HasSceneSettings, "RC1 conversion preserves input and supplies scene settings");
    }

    private static void VerifyGc()
    {
        var wad = Fixture("gc-content");
        var conversion = LevelContentConverter.Convert(GameId.GC, GameId.UYA, wad);
        Check(!conversion.HasSceneSettings, "GC environment settings do not claim a complete native scene");
        var inventory = LevelContentInventoryReader.ReadConverted(GameId.GC, GameId.UYA, conversion);
        Check(inventory.Single(family => family.Family == "ties").Items.Count == 2, "GC inventory counts instances");
        Check(!inventory.Single(family => family.Family == "mobys").Present, "synthesized native tables do not hide absent donor families");
        Check(!BinaryMagic.IsWad(conversion.Files.Single(file => file.Path == "assets/asset_wad.bin").Bytes), "GC shares a decoded asset buffer");
        var overlay = SkyOverlay();
        var expanded = new byte[0x2000];
        wad.AsSpan(0, 0x1000).CopyTo(expanded);
        wad.AsSpan(0x1000).CopyTo(expanded.AsSpan(0x1800));
        BinaryPrimitives.WriteInt32LittleEndian(expanded.AsSpan(0x14), 2);
        BinaryPrimitives.WriteInt32LittleEndian(expanded.AsSpan(0x20), 3);
        BinaryPrimitives.WriteInt32LittleEndian(expanded.AsSpan(0x800), 0x800);
        BinaryPrimitives.WriteInt32LittleEndian(expanded.AsSpan(0x804), overlay.Length);
        overlay.CopyTo(expanded, 0x1000);
        var motion = LevelContentConverter.Convert(GameId.GC, GameId.UYA, expanded).SkyAngularVelocitiesRadiansPerSecond[1];
        Check(Math.Abs(motion.X - 0.012f) < 0.000001f && Math.Abs(motion.Z + 0.006f) < 0.000001f,
            "SDK output normalizes GC angular velocity to radians per second");
    }

    private static void VerifyWriters()
    {
        var original = UyaGameplayInstanceTemplates.CreateMoby(123, 42);
        var native = GameplayInstanceConverter.RemapMobyForImport(GameId.UYA, original, 123, 99, 321, 4);
        var parsed = UyaMobyInstancesReader.ReadInstance(native);
        Check(parsed.Uid == 99 && parsed.ClassId == 321 && parsed.Group == 4, "native import remapping owns UID/class/group fields");
        for (var index = 0; index < original.Length; index++)
            if (index is not (>= 0x10 and < 0x14 or >= 0x28 and < 0x2c or >= 0x58 and < 0x5c))
                Check(original[index] == native[index], "remapping preserves unowned native bytes");
        Check(UyaMobyInstancesReader.ReadInstance(original).ClassId == 123, "remapping preserves its input");
        Reject<InvalidDataException>(() => GameplayInstanceConverter.RemapMobyForImport(GameId.UYA, original, 999, 99, 321, 4));
        Reject<ArgumentOutOfRangeException>(() => GameplayInstanceConverter.RemapMobyForImport(GameId.UYA, original, 123, 99, 65536, 4));
        var record = GameplayInstanceConverter.CreateGrindPathRecord(GameId.UYA, 11, 22, 33);
        Check(record.Length == 0x20 && BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(0x10)) == 11
            && BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(0x14)) == 22
            && BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(0x18)) == 33, "grind path records own their native layout");
    }

    private static void VerifyTextureRepair()
    {
        var pif = PifWriter.CreateIndexed8(32, 32, new byte[1024], new byte[1024]);
        var old = new FrontendAssetTexture(0, PifWriter.Write(pif));
        var updated = old with { PifBytes = PifWriter.Write(new(pif.Header, pif.Encoding, pif.PaletteData, pif.PixelData,
            [Enumerable.Repeat((byte)2, 256).ToArray(), Enumerable.Repeat((byte)3, 64).ToArray()])) };
        var native = old with { PifBytes = LevelAssetModelConverter.ConvertTexture(GameId.RC1, GameId.UYA, old.PifBytes) };
        var repaired = LevelAssetModelConverter.RepairTextureUpdate(GameId.RC1, GameId.UYA, [old], [updated], [native]);
        Check(repaired is not null && PifReader.Read(repaired[0].PifBytes).Header.MipLevels == 3, "repair restores authored mipmaps");
        var custom = native with { PifBytes = PifWriter.Write(PifWriter.CreateIndexed8(32, 32, new byte[1024], Enumerable.Repeat((byte)9, 1024).ToArray())) };
        Check(LevelAssetModelConverter.RepairTextureUpdate(GameId.RC1, GameId.UYA, [old], [updated], [custom]) is null, "repair preserves independent edits");
        Check(!LevelAssetModelConverter.MatchesTextureBaseUpdate(GameId.RC1, [old], [custom]), "different base images cannot repair an extraction");
        Check(LevelAssetModelConverter.RepairTextureUpdate(GameId.RC1, GameId.UYA, [old], [updated], repaired!) is null, "repair is idempotent");
        Reject<OperationCanceledException>(() => LevelAssetModelConverter.RepairTextureUpdate(GameId.RC1, GameId.UYA, [old], [updated], [native], new CancellationToken(true)));
    }

    private static void CheckSkyTextures(byte[] bytes)
    {
        var sky = SkyboxReader.Read(new MemoryStream(bytes, writable: false), GameId.UYA);
        var texture = sky.Textures.Single();
        Check(Enumerable.Range(0, 256).All(index => texture.PixelData[TextureConverter.RemapPixelIndexFromRac4(index, 16)] == (byte)index),
            "DL sky textures are linear in the native payload used by preview and packing");
        Check(texture.PaletteData.SequenceEqual(Enumerable.Range(0, 0x400).Select(index => (byte)(index * 3))),
            "sky unswizzling preserves the palette");
    }


    private static byte[] SkyOverlay()
    {
        var data = new byte[0x200]; var code = new byte[0x200];
        void W(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(offset), value);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(0x10c), 0.0002f);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(0x114), -0.0001f);
        W(0, 0x27BDFFC0); W(4, 0x2403000C); W(8, 0xFFB10018); W(12, 0x00838818);
        foreach (var offset in new[] { 0x14, 0x44, 0x68 }) { W(offset, 0x3C02001B); W(offset + 4, 0x8C421230); }
        W(0x100, 0x3C020020); W(0x104, 0x24420100); W(0x108, 0xAF820000u | (ushort)(0x001B1230 - 0x001AEFF0));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var (address, bytes) in new[] { (0x00200000u, data), (0x00300000u, code) })
        { writer.Write(address); writer.Write(bytes.Length); writer.Write(1); writer.Write(0); writer.Write(bytes); }
        return stream.ToArray();
    }

    private static void VerifyGcRawInventory(byte[] wad)
    {
        var inventory = LevelContentInventoryReader.Read(GameId.GC, wad);
        Check(inventory.Count == 18, "all donor families are inventoried");
        Check(Family("ties").Items.Count == 2 && Family("ties").Items.All(value => value.ClassId == 200),
            "counts gameplay instances rather than model definitions");
        Check(Family("shrubs").Present && Family("shrubs").Items.Count == 0, "empty shrub table");
        Check(!Family("mobys").Present, "absent Moby table");
        Check(Family("cuboids").Items.Count == 1 && Family("levelSettings").Items.Count == 1, "GC geometry and settings");
        var malformedWad = (byte[])wad.Clone();
        Write(malformedWad, 0x1000 + 0xa0, int.MaxValue);
        var malformed = LevelContentInventoryReader.Read(GameId.GC, malformedWad);
        Check(malformed.Single(value => value.Family == "ties").Diagnostic is not null,
            "malformed instances cannot become selectable");
        Check(malformed.Single(value => value.Family == "cuboids").Items.Count == 1,
            "one malformed family does not hide valid donor geometry");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Reject<OperationCanceledException>(() => LevelContentInventoryReader.Read(GameId.GC, wad, cancelled.Token));
        Reject<NotSupportedException>(() => LevelContentInventoryReader.Read(GameId.UYA, wad));
        LevelContentFamily Family(string name) => inventory.Single(value => value.Family == name);
    }

    private static void VerifyStreamedInventory(byte[] original)
    {
        var wad = new byte[0x2800];
        original.CopyTo(wad, 0);
        Write(wad, 0x84c, 0x180);
        var terrain = SyntheticTerrain(1);
        terrain.CopyTo(wad, 0xb60);
        for (var index = 0; index < 2; index++)
        {
            Write(wad, 0x30 + index * 8, 3 + index);
            Write(wad, 0x34 + index * 8, 1);
            Write(wad, (3 + index) * 0x800, 0x10);
            terrain.CopyTo(wad, (3 + index) * 0x800 + 0x10);
        }
        var inventory = LevelContentInventoryReader.Read(GameId.GC, wad);
        Check(inventory.Single(value => value.Family == "tfrags").Items.Sum(value => value.ItemCount) == 2,
            "resident chunk zero is not counted twice: "
                + inventory.Single(value => value.Family == "tfrags").Diagnostic);
        var conversion = LevelContentConverter.Convert(GameId.GC, GameId.UYA, wad);
        Check(conversion.ChunkSlotCount == 3
            && conversion.Files.Any(file => file.Path == "level_wad/chunks/chunk1.wad")
            && conversion.Files.All(file => file.Path != "level_wad/chunks/chunk2.wad"),
            "streamed owners keep their native chunk slots");
    }

    private static void VerifySparseTieMappings(byte[] original)
    {
        var wad = original.ToArray();
        Write(wad, 0x1000 + 0x90, 0x280);
        Write(wad, 0x1284, 1);
        Write(wad, 0x1290, 5);
        Write(wad, 0x1110 + UyaTieInstancesReader.OcclusionIdOffset, 99);
        Write(wad, 0xb60 + 0x0c, 7);
        var converted = LevelContentConverter.Convert(GameId.GC, GameId.UYA, wad);
        var mappings = UyaOcclusionMappingsReader.Read(converted.Files.Single(file => file.Path == "gameplay/core/occlusion.bin").Bytes);
        Check(mappings.Ties.Select(mapping => (mapping.BitIndex, mapping.OcclusionId)).SequenceEqual([(5, 0), (-1, 99)]),
            "sparse TIE mappings expand in instance order with uncullable slots for unmapped instances");
        Check(mappings.Tfrags.Count == 7 && mappings.Tfrags.All(mapping => mapping.BitIndex == -1),
            "unmapped Museum terrain groups remain uncullable");
    }

    private static void VerifyAppendedMappings()
    {
        var terrain = SyntheticTerrain(1);
        BinaryPrimitives.WriteInt16LittleEndian(terrain.AsSpan(0x40 + 0x3a), 1);
        var mappings = new byte[0x10 + 5 * 8];
        Write(mappings, 0, 2); Write(mappings, 4, 2); Write(mappings, 8, 1);
        Write(mappings, 0x10, -1); Write(mappings, 0x18, 9); Write(mappings, 0x34, 11);
        var appended = TfragTerrainComposer.AppendNativeTerrain(GameId.UYA, terrain, terrain);
        var composed = TfragTerrainComposer.ComposeOcclusionMappings(GameId.UYA, appended, terrain, mappings, 1, null);
        var finalized = TfragTerrainComposer.FinalizeTerrain(GameId.UYA, appended,
            new Dictionary<int, IReadOnlyDictionary<int, int>>(), composed.FirstCustomMappingIndex,
            firstGeneratedGroupIndex: composed.FirstCustomGroupIndex);
        Check(TfragTerrainReader.Read(finalized).Chunks.Select(chunk => chunk.OcclusionIndex).SequenceEqual(new short[] { 1, 2 })
            && UyaOcclusionMappingsReader.Read(composed.OcclusionBytes).Tfrags[2].BitIndex == 9,
            "appended terrain follows source mapping slots without overwriting streamed mappings");
        var relocated = TfragTerrainComposer.ComposeOcclusionMappings(GameId.UYA, appended, terrain, mappings, 1, null,
            appendedSourceMappingIndices: [0]);
        Check(UyaOcclusionMappingsReader.Read(relocated.OcclusionBytes).Tfrags[2].BitIndex == -1,
            "relocated donor terrain retains its authored uncullable mapping rather than borrowing a nearby group");

    }

    private static byte[] SyntheticTerrain(int groupCount = 2)
    {
        const int tableOffset = 0x40;
        const int recordSize = 0x40;
        const int dataSize = 0x10;
        var bytes = new byte[tableOffset + recordSize * groupCount + dataSize * groupCount];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x00, 4), tableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x04, 4), groupCount);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x08, 4), BitConverter.SingleToInt32Bits(32f));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x0C, 4), 2);
        for (var index = 0; index < groupCount; index++)
        {
            var record = tableOffset + index * recordSize;
            var data = tableOffset + recordSize * groupCount + index * dataSize;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record, 4),
                BitConverter.SingleToInt32Bits((index * 3 + 1) * 1024f));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 4, 4),
                BitConverter.SingleToInt32Bits((index * 3 + 2) * 1024f));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 8, 4),
                BitConverter.SingleToInt32Bits((index * 3 + 3) * 1024f));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 0x0C, 4),
                BitConverter.SingleToInt32Bits(32f));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(record + 0x10, 4), data - tableOffset);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(record + 0x2E, 2), -1);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(record + 0x30, 2), -1);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(record + 0x38, 2), -1);
            bytes.AsSpan(data, dataSize).Fill(0xA5);
        }
        return bytes;
    }

    private static void Write(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], value);

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
}
