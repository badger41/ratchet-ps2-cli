using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gameplay;
using RatchetPs2.Core.Hud;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Palettes;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Png;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Ties;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Armor;
using RatchetPs2.Games.DL.Gameplay;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.DL.Moby;
using RatchetPs2.Games.DL.Online;
using RatchetPs2.Games.GC.Gameplay;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.GC.Skyboxes;
using RatchetPs2.Games.RC1.Gameplay;
using RatchetPs2.Games.RC1.Level;
using RatchetPs2.Games.RC1.Ties;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Sdk;

if (args is ["--qualify-uya-iso", var isoPath, var reportPath])
{
    var report = UyaArchiveQualification.Run(isoPath);
    UyaArchiveQualification.Write(report, reportPath);
    Console.WriteLine($"Qualified {report.PassedLevelCount}/{report.LevelCount} UYA levels: {reportPath}");
    Environment.ExitCode = report.PassedLevelCount == report.LevelCount ? 0 : 1;
    return;
}

if (args.Contains("--uya-sdk-archive", StringComparer.Ordinal))
{
    ValidateUyaLevelArchiveBuilder();
    ValidateUyaLevelAssetComposer();
    ValidateIsoPatchPlanning();
    ValidateUyaArchiveQualificationCorpus();
    Console.WriteLine("UYA SDK archive workflow tests passed.");
    return;
}

if (args.Contains("--uya-texture-inventory", StringComparer.Ordinal))
{
    ValidateTextureInventory();
    ValidatePaletteOptimization();
    ValidateUyaStaticAssetComposition();
    Console.WriteLine("UYA texture inventory and palette optimizer tests passed.");
    return;
}

if (args.Contains("--uya-static-instances", StringComparer.Ordinal))
{
    ValidateUyaGameplayTypedParsing();
    ValidateUyaStaticInstanceParsing();
    Console.WriteLine("UYA instance writer round-trip tests passed.");
    return;
}

if (args.Contains("--wad-compression", StringComparer.Ordinal))
{
    ValidateWadCompression();
    Console.WriteLine("WAD compression tests passed.");
    return;
}

if (args.Contains("--uya-inventory", StringComparer.Ordinal))
{
    ValidateUyaLevelWadInventory();
    ValidateUyaLevelWadInventoryWhenAvailable();
    Console.WriteLine("UYA level WAD inventory tests passed.");
    return;
}

ValidateLevelInfoLookup();
ValidateLevelWadParsing();
ValidateArmorWadParsing();
ValidateOnlineWadExtraction();
ValidateOnlineArmorWadParsing();
ValidateLooseLevelWadExtraction();
ValidateLooseLevelWadUnpacking();
ValidateRc1IsoLevelExtraction();
ValidateRc1LevelAssetProfile();
ValidateRc1TieHeaderParsing();
ValidateRc1LevelSettingsParsing();
ValidateRc1TieInstanceConversion();
ValidateRc1RenderPackageLightingRouting();
ValidateRc1MobyParsing();
ValidateGcLevelInfoLookup();
ValidateGcSkyRotationParsing();
ValidateUyaLevelInfoLookup();
ValidateUyaLevelWadParsing();
ValidateUyaLevelWadInventory();
ValidateUyaLevelWadInventoryWhenAvailable();
ValidateUyaLevelArchiveBuilder();
ValidateUyaLevelAssetComposer();
ValidateIsoPatchPlanning();
ValidateTextureInventory();
ValidatePaletteOptimization();
ValidateUyaStaticAssetComposition();
ValidateUyaArchiveQualificationCorpus();
ValidateUyaLooseLevelWadExtraction();
ValidateUyaDetachedWadExtraction();
ValidateUyaLooseLevelWadUnpacking();
ValidateUyaStandaloneLevelDataUnpacking();
ValidateUyaStandaloneGameplayUnpacking();
ValidateUyaCustomMapZipUnpacking();
ValidateUyaGameplayTypedParsing();
ValidateUyaStaticInstanceParsing();
ValidateGameplayGeometryParsing();
ValidateUyaGameplayLightingParsing();
ValidateUyaAssetRenderPackageBuild();
ValidateChunkTfragAssetRenderPackageWhenAvailable();
ValidateChunkTfragWadReaderWhenAvailable();
ValidateLooseLevelWadRenderPackageWhenAvailable();
ValidateUyaLooseLevelWadRenderPackageWhenAvailable();
ValidateLooseLevelWadFailures();
ValidateMissionPlaceholderDetection();
ValidateMissionMobyBankParsing();
ValidateLevelSceneWadEmptyDetection();
ValidateWadCompression();
ValidateCoreLevelSegments();
ValidateGameplayLevelSettingsParsing();
ValidateGameplayMobyInstancesParsing();
ValidateCodeSegmentParsing();
ValidateHudBankParsing();
ValidateWorldInstanceParsing();
ValidateAssetSlicing();
ValidateEnvironmentTextureRenderPackage();
ValidateMobyGsStashTextures();
ValidateDzoMobyExportConventions();
ValidateDzoMetalAndGlowExport();
ValidateDzoTeamTextureVariants();
ValidateDzoTextureAlphaModes();
ValidateDzoGlbExportWhenAvailable();
ValidatePifMipRoundtrip();
ValidateNormalizedTextureArtifacts();

Console.WriteLine("Level extraction tests passed.");

static void ValidateRc1IsoLevelExtraction()
{
    const int levelId = 0;
    const int amalgamatedHeaderSector = 1550;
    const int levelDataSector = 1600;
    const int gameplayNtscSector = 1602;
    const int gameplayPalSector = 1603;
    const int occlusionSector = 1604;
    const int audioDataSector = 1610;
    const int musicSector = 1611;
    const int sceneSoundSector = 1620;
    const int sceneWadSector = 1621;

    var iso = new byte[1630 * Rc1LevelConstants.SectorSize];
    var tocOffset = Rc1LevelConstants.TableOfContentsSector * Rc1LevelConstants.SectorSize;
    WriteInt32(iso, tocOffset, 1);
    WriteInt32(iso, tocOffset + 4, Rc1LevelConstants.TableOfContentsSize);
    WriteInt32(iso, tocOffset + Rc1LevelConstants.LevelTableOffset, amalgamatedHeaderSector);
    WriteInt32(iso, tocOffset + Rc1LevelConstants.LevelTableOffset + 4, 100);

    var headerOffset = amalgamatedHeaderSector * Rc1LevelConstants.SectorSize;
    WriteInt32(iso, headerOffset, levelId);
    WriteInt32(iso, headerOffset + 0x04, Rc1LevelConstants.AmalgamatedHeaderSize);
    WriteSectorRange(iso, headerOffset + 0x08, levelDataSector, 2);
    WriteSectorRange(iso, headerOffset + 0x10, gameplayNtscSector, 1);
    WriteSectorRange(iso, headerOffset + 0x18, gameplayPalSector, 1);
    WriteSectorRange(iso, headerOffset + 0x20, occlusionSector, 1);
    WriteSectorRange(iso, headerOffset + 0x28, audioDataSector, 4);
    WriteInt32(iso, headerOffset + 0x148, musicSector);
    WriteInt32(iso, headerOffset + 0x184, sceneSoundSector);
    WriteInt32(iso, headerOffset + 0x184 + 0x18, sceneWadSector);

    var levelDataOffset = levelDataSector * Rc1LevelConstants.SectorSize;
    WriteSectorRange(iso, levelDataOffset + 0x00, 0x80, 4);
    WriteSectorRange(iso, levelDataOffset + 0x08, 0x90, 4);
    WriteSectorRange(iso, levelDataOffset + 0x10, 0xa0, 4);
    WriteSectorRange(iso, levelDataOffset + 0x18, 0xb0, 4);
    WriteSectorRange(iso, levelDataOffset + 0x20, 0xc0, 4);
    WriteSectorRange(iso, levelDataOffset + 0x28, 0xd0, 4);
    WriteSectorRange(iso, levelDataOffset + 0x50, 0xe0, 4);
    iso[levelDataOffset + 0x80] = 0x11;
    iso[levelDataOffset + 0x90] = 0x21;
    iso[levelDataOffset + 0xa0] = 0x31;
    iso[levelDataOffset + 0xb0] = 0x41;
    iso[levelDataOffset + 0xc0] = 0x51;
    iso[levelDataOffset + 0xd0] = 0x61;
    iso[levelDataOffset + 0xe0] = 0x71;
    iso[gameplayNtscSector * Rc1LevelConstants.SectorSize] = 0x81;
    iso[gameplayPalSector * Rc1LevelConstants.SectorSize] = 0x91;
    iso[occlusionSector * Rc1LevelConstants.SectorSize] = 0xa1;
    iso[audioDataSector * Rc1LevelConstants.SectorSize] = 0xb1;

    "VAGp"u8.CopyTo(iso.AsSpan(musicSector * Rc1LevelConstants.SectorSize));
    "VAGp"u8.CopyTo(iso.AsSpan(sceneSoundSector * Rc1LevelConstants.SectorSize));
    "WAD"u8.CopyTo(iso.AsSpan(sceneWadSector * Rc1LevelConstants.SectorSize));
    WriteInt32(iso, (sceneWadSector * Rc1LevelConstants.SectorSize) + 3, 0x10);

    using var stream = new MemoryStream(iso, writable: false);
    var extracted = Rc1LooseLevelWadExtractor.ExtractAll(stream, levelId);
    Expect(extracted.Level.LevelInfo.TableIndex == 0, "RC1 level lookup should retain the ToC table index");
    Expect(extracted.Level.PayloadBaseSector == levelDataSector - 1, "RC1 primary WAD should reserve one header sector before its first payload");
    Expect(extracted.Level.SectorCount == 6, "RC1 primary WAD should span all four absolute payload ranges");
    Expect(extracted.Audio.Length == 3 * Rc1LevelConstants.SectorSize, "RC1 audio extraction should synthesize a relative header and preserve its data");
    Expect(extracted.Scene.Length == 7 * Rc1LevelConstants.SectorSize, "RC1 scene extraction should reserve five header sectors and preserve scene data");

    var package = Rc1LevelWadUnpacker.Unpack(extracted.Level.Bytes);
    var files = package.Files.ToDictionary(file => file.Path);
    Expect(files["code/code.bin"].Bytes[0] == 0x11, "RC1 unpack should expose the level overlay");
    Expect(files["level_wad/sound.bnk"].Bytes[0] == 0x21, "RC1 unpack should expose the level sound bank");
    Expect(files["assets/asset_header.bin"].Bytes[0] == 0x31, "RC1 unpack should expose the asset header");
    Expect(files["assets/palette.bin"].Bytes[0] == 0x41, "RC1 unpack should expose GS RAM palette data");
    Expect(files["assets/asset_wad.bin"].Bytes[0] == 0x71, "RC1 unpack should expose the asset WAD");
    Expect(files["gameplay/gameplay_core.bin"].Bytes[0] == 0x81, "RC1 unpack should expose NTSC gameplay");
    Expect(files["gameplay/gameplay_pal_core.bin"].Bytes[0] == 0x91, "RC1 unpack should preserve PAL gameplay");
    Expect(files["occlusion/occlusion.bin"].Bytes[0] == 0xa1, "RC1 unpack should expose occlusion data");

    var texture = DlAssetReader.BuildAssetTexture(
        "rc1",
        0,
        new DlAssetTextureDefinition(0, 0, 2, 2, 0, 0, 0, -1),
        new byte[0x400],
        [1, 2, 3, 4],
        0,
        isSwizzled: false,
        useTextureFlags: false);
    Expect(texture.Metadata.PixelLength == 4, "RC1 texture entries should use asset data without later-game stash or mipmap flags");
}

static void ValidateRc1TieHeaderParsing()
{
    var bytes = new byte[0x140];
    WriteInt32(bytes, 0x00, 0x100);
    WriteInt32(bytes, 0x04, 0x110);
    WriteInt32(bytes, 0x08, 0x120);
    WriteSingle(bytes, 0x10, 10f);
    WriteSingle(bytes, 0x14, 20f);
    WriteSingle(bytes, 0x18, 30f);
    bytes[0x20] = 0;
    bytes[0x21] = 0;
    bytes[0x22] = 0;
    bytes[0x23] = 0;
    WriteInt32(bytes, 0x2c, 0x130);
    WriteSingle(bytes, 0x40, 0.5f);

    var profile = Rc1TieGameProfile.Default;
    var tie = TieClassReader.Read(bytes, TieClassReadOptions.ForGameProfile(profile));

    Expect(tie.Header.PacketTableOffsets.SequenceEqual([0x100u, 0x110u, 0x120u]), "RC1 tie packet offsets should come from 0x00");
    Expect(tie.Header.ShadersOffset == 0x130, "RC1 tie shader offset should come from 0x2c");
    Expect(tie.Header.NearDistance == 10f && tie.Header.FarDistance == 30f, "RC1 tie LOD distances should come from 0x10");
    Expect(tie.Header.Scale == 0.5f, "RC1 tie scale should come from 0x40");
}

static void ValidateRc1LevelAssetProfile()
{
    var header = new byte[0xc0];
    WriteInt32(header, 0x04, header.Length);
    WriteInt32(header, 0x84, 1);
    WriteInt32(header, 0xac, 0xb0);

    var files = DlLevelWadRenderPackageBuilder.BuildAssetFiles(
        GameId.RC1,
        levelIndex: 1,
        header,
        paletteBytes: [],
        assetBytes: [],
        DlLevelWadRenderPackageBuildOptions.Default with
        {
            AssetProfile = Rc1LevelAssetProfile.Default
        });

    Expect(
        files.Any(file => file.Path == "assets/manifest.json"),
        "RC1 asset profile should ignore later-game mipmap and GS stash header fields");
    Expect(
        Rc1LevelAssetProfile.Default.MobyModelFormat == MobyModelFormat.Rc1
            && !Rc1LevelAssetProfile.Default.UseTextureFlags
            && Rc1LevelAssetProfile.Default.TieGameProfile == Rc1TieGameProfile.Default,
        "RC1 asset profile should select the RC1 model, texture, and TIE formats");
}

static void ValidateRc1LevelSettingsParsing()
{
    var bytes = new byte[Rc1LevelSettingsReader.Size];
    WriteInt32(bytes, 0x00, 100);
    WriteInt32(bytes, 0x04, 255);
    WriteInt32(bytes, 0x08, 255);
    WriteInt32(bytes, 0x0c, 105);
    WriteInt32(bytes, 0x10, 127);
    WriteInt32(bytes, 0x14, 180);
    WriteSingle(bytes, 0x18, 0);
    WriteSingle(bytes, 0x1c, 245760);
    WriteSingle(bytes, 0x20, 255);
    WriteSingle(bytes, 0x24, 102);

    var settings = Rc1LevelSettingsReader.Read(bytes);
    Expect(
        settings.FogColor == new Rc1Rgb96(105, 127, 180)
            && settings.FogNearDistance == 0
            && settings.FogFarDistance == 245760
            && settings.FogNearIntensity == 255
            && settings.FogFarIntensity == 102,
        "RC1 level settings should decode gameplay fog fields");
}

static void ValidateRc1TieInstanceConversion()
{
    var source = new byte[0x10 + Rc1Gameplay.TieInstanceSize];
    WriteInt32(source, 0, 1);
    WriteInt32(source, 0x10, 0x7e4);
    WriteSingle(source, 0x20, 1f);
    source[0x60] = 0x34;
    source[0x61] = 0x12;
    WriteInt32(source, 0xe0, 9);

    var converted = Rc1Gameplay.ConvertTieInstances(source);

    Expect(converted.Instances.Length == 0x70, "RC1 ties should convert from 0xe0-byte to 0x60-byte instance records");
    Expect(BinaryPrimitives.ReadInt32LittleEndian(converted.Instances.AsSpan(0x10)) == 0x7e4, "RC1 tie conversion should preserve class ids");
    Expect(BinaryPrimitives.ReadInt32LittleEndian(converted.Instances.AsSpan(0x60)) == 9, "RC1 tie conversion should move lighting fields after the matrix");
    Expect(BinaryPrimitives.ReadUInt16LittleEndian(converted.AmbientRgbas.AsSpan(4)) == 0x1234, "RC1 tie conversion should preserve embedded ambient colors");
}

static void ValidateRc1RenderPackageLightingRouting()
{
    ExpectThrows<InvalidDataException>(() => Rc1LevelWadRenderPackageBuilder.GetAssetSources([]));

    var pointLights = new byte[0x30];
    WriteInt32(pointLights, 0, 1);
    var files = Rc1LevelWadRenderPackageBuilder.BuildFiles(
        1,
        [
            new PackedFile("assets/asset_header.bin", [1], "application/octet-stream"),
            new PackedFile("assets/palette.bin", [1], "application/octet-stream"),
            new PackedFile("assets/asset_wad.bin", [1], "application/octet-stream"),
            new PackedFile("gameplay/core/directional_lights.bin", new byte[0x10], "application/octet-stream"),
            new PackedFile("gameplay/core/point_lights.bin", pointLights, "application/octet-stream")
        ],
        _ => [new PackedFile("assets/manifest.json", "{}"u8.ToArray(), "application/json")]);

    Expect(
        files.Any(file => file.Path == "world/lighting/point_lights.bin"),
        "RC1 render packages should preserve their point-light table");
    using var worldManifest = JsonDocument.Parse(files.Single(file => file.Path == "world/manifest.json").Bytes);
    Expect(
        worldManifest.RootElement.GetProperty("PointLightCount").GetInt32() == 1,
        "RC1 world manifest should expose the point-light count");
}

static void ValidateRc1MobyParsing()
{
    var instancesBytes = new byte[Rc1MobyInstancesReader.HeaderSize + Rc1MobyInstancesReader.RecordSize];
    WriteInt32(instancesBytes, 0, 1);
    WriteInt32(instancesBytes, 4, 32);
    var recordOffset = Rc1MobyInstancesReader.HeaderSize;
    WriteInt32(instancesBytes, recordOffset, Rc1MobyInstancesReader.RecordSize);
    WriteInt32(instancesBytes, recordOffset + 0x18, 1007);
    WriteSingle(instancesBytes, recordOffset + 0x1c, 1.5f);
    WriteSingle(instancesBytes, recordOffset + 0x30, 10f);
    WriteSingle(instancesBytes, recordOffset + 0x34, 20f);
    WriteSingle(instancesBytes, recordOffset + 0x38, 30f);
    WriteInt32(instancesBytes, recordOffset + 0x58, 7);

    var instances = Rc1MobyInstancesReader.Read(instancesBytes);
    Expect(instances.StaticCount == 1 && instances.SpawnableMobyCount == 32, "RC1 moby instance counts should be parsed");
    Expect(instances.Instances[0].ClassId == 1007, "RC1 moby class ids should come from 0x18");
    Expect(instances.Instances[0].Position == new Rc1Vector3(10f, 20f, 30f), "RC1 moby positions should come from 0x30");
    Expect(instances.Instances[0].PvarIndex == 7, "RC1 moby pvar indices should come from 0x58");

    var modelBytes = new byte[0xf0];
    WriteInt32(modelBytes, 0, 0x48);
    modelBytes[4] = 1;
    modelBytes[0x0a] = 0xff;
    modelBytes[0x0b] = 0xff;
    WriteSingle(modelBytes, 0x24, 1f);
    WriteInt32(modelBytes, 0x48 + 8, 0x60);
    modelBytes[0x48 + 0x0c] = 9;
    modelBytes[0x48 + 0x0f] = 3;
    WriteUInt32(modelBytes, 0x60 + 0x0c, 3);
    WriteUInt32(modelBytes, 0x60 + 0x14, 3);
    WriteUInt32(modelBytes, 0x60 + 0x18, 0x20);
    WriteUInt32(modelBytes, 0x60 + 0x1c, 0x90);

    using var modelStream = new MemoryStream(modelBytes, writable: false);
    var model = MobyModelReader.Read(modelStream, new MobyModelReadOptions { ModelFormat = MobyModelFormat.Rc1 });
    var vertexData = model.MeshTable!.Entries.Single().VertexData;
    Expect(model.FarLodMeshCount == 0 && model.TeamPalettes == 0, "RC1 header bytes 0x0a/0x0b should not become later-game mesh fields");
    Expect(vertexData.Length == 0x80, "RC1 32-bit moby vertex headers should normalize to the shared compact layout");
    Expect(BinaryPrimitives.ReadUInt16LittleEndian(vertexData.AsSpan(6)) == 3, "RC1 moby main vertex count should be preserved");
    Expect(BinaryPrimitives.ReadUInt16LittleEndian(vertexData.AsSpan(0x0c)) == 0x10, "RC1 moby vertex table offset should account for the compact header");
}

static void ValidateArmorWadParsing()
{
    const int payloadSector = 1100;
    const int armorIndex = 3;
    var header = new byte[DlArmorWadReader.StandardHeaderSize];
    WriteInt32(header, 0x00, header.Length);
    WriteInt32(header, 0x04, payloadSector);
    var armorEntryOffset = 0x08 + (armorIndex * 0x10);
    WriteInt32(header, armorEntryOffset + 0x00, 0);
    WriteInt32(header, armorEntryOffset + 0x04, 1);
    WriteInt32(header, armorEntryOffset + 0x08, 1);
    WriteInt32(header, armorEntryOffset + 0x0c, 1);

    var payload = new byte[2 * DlLevelConstants.SectorSize];
    payload[0] = 0x42;
    var texture = PifWriter.CreateIndexed8(8, 8, new byte[0x400], new byte[64]);
    var pifBytes = PifWriter.Write(texture);
    var textureListOffset = DlLevelConstants.SectorSize;
    WriteInt32(payload, textureListOffset, 1);
    WriteInt32(payload, textureListOffset + 4, 0x10);
    pifBytes.CopyTo(payload.AsSpan(textureListOffset + 0x10));

    var armorWad = DlArmorWadReader.ReadPayload(header, payload);
    Expect(armorWad.HeaderSize == DlArmorWadReader.StandardHeaderSize, "expected standard DL armor header");
    Expect(armorWad.Armors.Count == 1, "expected one populated DL armor slot");
    Expect(armorWad.Armors[0].Index == armorIndex, "expected DL armor slot index to be retained");
    Expect(armorWad.Armors[0].ModelBytes[0] == 0x42, "expected DL armor model sector bytes");
    Expect(armorWad.Armors[0].PifTextures.Count == 1, "expected DL armor PIF material list");
    Expect(!armorWad.Armors[0].PifTextures[0].Header.IsSwizzled, "expected unswizzled DL armor texture");

    var iso = new byte[(payloadSector * DlLevelConstants.SectorSize) + payload.Length];
    header.CopyTo(iso.AsSpan(1001 * DlLevelConstants.SectorSize));
    payload.CopyTo(iso.AsSpan(payloadSector * DlLevelConstants.SectorSize));
    using var isoStream = new MemoryStream(iso, writable: false);
    var isoArmorWad = DlArmorWadReader.ReadFromIso(isoStream);
    Expect(isoArmorWad.Armors.Count == 1, "expected DL armor WAD discovery in the ISO global table");
    Expect(isoArmorWad.Armors[0].PifTextures.Count == 1, "expected DL armor texture extraction from ISO sectors");

    isoStream.Position = 0;
    using var extractedWadStream = new MemoryStream();
    var extraction = DlArmorWadReader.ExtractWadFromIso(isoStream, extractedWadStream);
    Expect(extraction.PayloadSectorCount == 2, "expected DL armor WAD payload extent from its sector ranges");
    Expect(
        extractedWadStream.Length == DlLevelConstants.SectorSize + payload.Length,
        "expected sector-padded DL armor WAD header followed by its payload");
    extractedWadStream.Position = 0;
    var extractedArmorWad = DlArmorWadReader.ReadWad(extractedWadStream);
    Expect(extractedArmorWad.Armors.Count == 1, "expected standalone DL armor WAD parsing");
    Expect(extractedArmorWad.Armors[0].ModelBytes[0] == 0x42, "expected standalone DL armor model bytes");
    Expect(extractedArmorWad.Armors[0].PifTextures.Count == 1, "expected standalone DL armor textures");

    var invalidHeader = header.ToArray();
    WriteInt32(invalidHeader, 0, 0x200);
    ExpectThrows<InvalidDataException>(() => DlArmorWadReader.ReadPayload(invalidHeader, payload));
}

static void ValidateOnlineWadExtraction()
{
    const int payloadSector = 1100;
    const int payloadSectorCount = 0x75e;
    var tocOffset = 1001 * DlLevelConstants.SectorSize;
    var spaceLikeHeader = new byte[DlOnlineWadExtractor.HeaderSize];
    WriteInt32(spaceLikeHeader, 0x00, spaceLikeHeader.Length);
    WriteInt32(spaceLikeHeader, 0x04, 1050);
    WriteInt32(spaceLikeHeader, 0x0c, 1);

    var onlineHeader = new byte[DlOnlineWadExtractor.HeaderSize];
    WriteInt32(onlineHeader, 0x00, onlineHeader.Length);
    WriteInt32(onlineHeader, 0x04, payloadSector);
    WriteInt32(onlineHeader, 0x0c, payloadSectorCount);

    var iso = new byte[(payloadSector + payloadSectorCount) * DlLevelConstants.SectorSize];
    spaceLikeHeader.CopyTo(iso.AsSpan(tocOffset));
    onlineHeader.CopyTo(iso.AsSpan(tocOffset + spaceLikeHeader.Length));
    iso[payloadSector * DlLevelConstants.SectorSize] = 0x5a;

    using var isoStream = new MemoryStream(iso, writable: false);
    using var wadStream = new MemoryStream();
    var extraction = DlOnlineWadExtractor.ExtractFromIso(isoStream, wadStream);
    Expect(extraction.SourcePayloadSector == payloadSector, "expected the DL online WAD header to be distinguished from the space WAD header");
    Expect(extraction.PayloadSectorCount == payloadSectorCount, "expected the complete DL online WAD payload extent");
    Expect(
        wadStream.Length == (payloadSectorCount + 1L) * DlLevelConstants.SectorSize,
        "expected sector-padded DL online WAD header followed by its payload");
    Expect(wadStream.GetBuffer()[DlLevelConstants.SectorSize] == 0x5a, "expected the DL online WAD payload bytes");
}

static void ValidateOnlineArmorWadParsing()
{
    const int armorIndex = 7;
    const int classId = 1234;
    const int modelOffset = 0x600;
    const int textureOffset = 0x800;
    var modelBytes = Enumerable.Range(0, 0x400).Select(value => (byte)(value & 0xff)).ToArray();
    var compressedModel = WadCompression.Compress(modelBytes);

    var texture = PifWriter.CreateIndexed8(8, 8, new byte[0x400], new byte[64]);
    var pifBytes = PifWriter.Write(texture);
    var textureList = new byte[0x10 + pifBytes.Length];
    WriteInt32(textureList, 0x00, 1);
    WriteInt32(textureList, 0x04, 0x10);
    pifBytes.CopyTo(textureList.AsSpan(0x10));
    var compressedTextures = WadCompression.Compress(textureList);

    var onlineData = new byte[2 * DlLevelConstants.SectorSize];
    var entryOffset = 0x250 + (armorIndex * 0x14);
    WriteInt32(onlineData, entryOffset + 0x00, classId);
    WriteInt32(onlineData, entryOffset + 0x04, modelOffset);
    WriteInt32(onlineData, entryOffset + 0x08, compressedModel.Length);
    WriteInt32(onlineData, entryOffset + 0x0c, textureOffset);
    WriteInt32(onlineData, entryOffset + 0x10, compressedTextures.Length);
    compressedModel.CopyTo(onlineData.AsSpan(modelOffset));
    compressedTextures.CopyTo(onlineData.AsSpan(textureOffset));

    var parsedData = DlOnlineArmorWadReader.ReadData(onlineData);
    Expect(parsedData.Armors.Count == 1, "expected one populated DL online armor slot");
    Expect(parsedData.Armors[0].Index == armorIndex, "expected the DL online armor slot index");
    Expect(parsedData.Armors[0].ClassId == classId, "expected the DL online armor class ID");
    Expect(parsedData.Armors[0].ModelBytes.SequenceEqual(modelBytes), "expected the DL online armor model to be decompressed");
    Expect(parsedData.Armors[0].PifTextures.Count == 1, "expected the DL online armor textures to be decompressed and parsed");

    var wad = new byte[DlLevelConstants.SectorSize + onlineData.Length];
    WriteInt32(wad, 0x00, DlOnlineWadExtractor.HeaderSize);
    WriteInt32(wad, 0x0c, onlineData.Length / DlLevelConstants.SectorSize);
    onlineData.CopyTo(wad.AsSpan(DlLevelConstants.SectorSize));
    using var wadStream = new MemoryStream(wad, writable: false);
    var parsedWad = DlOnlineArmorWadReader.ReadWad(wadStream);
    Expect(parsedWad.Armors.Count == 1, "expected standalone DL online WAD parsing");
    Expect(parsedWad.Armors[0].ClassId == classId, "expected standalone DL online armor class ID");
}

static void ValidateGcSkyRotationParsing()
{
    const uint velocityPointerAddress = 0x001B1230;
    var data = new byte[0x200];
    WriteSingle(data, 0x10c, 0.0002f);
    WriteSingle(data, 0x114, -0.0001f);

    var code = new byte[0x200];
    WriteUInt32(code, 0x00, 0x27BDFFC0);
    WriteUInt32(code, 0x04, 0x2403000C);
    WriteUInt32(code, 0x08, 0xFFB10018);
    WriteUInt32(code, 0x0c, 0x00838818);
    foreach (var offset in new[] { 0x14, 0x44, 0x68 })
    {
        WriteUInt32(code, offset, 0x3C02001B);
        WriteUInt32(code, offset + 4, 0x8C421230);
    }

    WriteUInt32(code, 0x100, 0x3C020020);
    WriteUInt32(code, 0x104, 0x24420100);
    WriteUInt32(code, 0x108, 0xAF820000u | (ushort)(velocityPointerAddress - 0x001AEFF0));

    using var overlay = new MemoryStream();
    using (var writer = new BinaryWriter(overlay, System.Text.Encoding.UTF8, leaveOpen: true))
    {
        WriteOverlaySegment(writer, 0x00200000, data);
        WriteOverlaySegment(writer, 0x00300000, code);
    }

    var rotations = GcSkyRotationReader.ReadRadiansPerFrame(overlay.ToArray());
    Expect(rotations.Count == 1 && rotations.ContainsKey(1), "expected GC overlay shell rotation table");
    var shell = rotations[1];
    Expect(MathF.Abs(shell.X - 0.0002f) < 0.0000001f && MathF.Abs(shell.Z + 0.0001f) < 0.0000001f,
        "expected exact GC shell angular velocity");
}

static void WriteOverlaySegment(BinaryWriter writer, uint address, byte[] data)
{
    writer.Write(address);
    writer.Write(data.Length);
    writer.Write(1);
    writer.Write(0);
    writer.Write(data);
}

static void ValidateLevelInfoLookup()
{
    var iso = new byte[DlLevelConstants.RetailLevelInfoTableOffset
        + (DlLevelConstants.LevelInfoCount * DlLevelConstants.LevelInfoSize)
        + DlLevelConstants.SectorSize];

    WriteLevelInfoEntry(
        iso,
        1,
        audio: new DlFileBlock(20, 1),
        level: new DlFileBlock(21, 1),
        scene: new DlFileBlock(22, 1));
    WriteLevelInfoEntry(
        iso,
        0x15,
        audio: new DlFileBlock(30, 1),
        level: new DlFileBlock(10, 2),
        scene: new DlFileBlock(31, 1));

    iso[10 * DlLevelConstants.SectorSize] = 0x42;

    using var stream = new MemoryStream(iso, writable: false);
    var levelSet = DlLevelInfoReader.ReadLevelSet(stream, 0x15);

    Expect(levelSet.RequestedLevelIndex == 0x15, "requested level index should be preserved");
    Expect(levelSet.MediaLevelIndex == 1, "level 0x15 should normalize to media level 1");
    Expect(levelSet.RequestedLevel.LevelWad == new DlFileBlock(10, 2), "requested level WAD block should come from requested levelinfo");
    Expect(levelSet.MediaLevel.LevelAudioWad == new DlFileBlock(20, 1), "audio WAD block should come from normalized media level");
    Expect(levelSet.MediaLevel.LevelSceneWad == new DlFileBlock(22, 1), "scene WAD block should come from normalized media level");

    stream.Position = 0;
    var levelWadBytes = DlLevelInfoReader.ReadSectorBlock(stream, levelSet.RequestedLevel.LevelWad);
    Expect(levelWadBytes.Length == DlLevelConstants.SectorSize * 2, "sector block read should return sector-scaled length");
    Expect(levelWadBytes[0] == 0x42, "sector block read should seek to the requested sector");

    stream.Position = 0;
    var levelWadHeaderBytes = DlLevelInfoReader.ReadSectorHeader(stream, levelSet.RequestedLevel.LevelWad, 1);
    Expect(levelWadHeaderBytes.Length == DlLevelConstants.SectorSize, "fixed sector header read should ignore fileblock length");
    Expect(levelWadHeaderBytes[0] == 0x42, "fixed sector header read should seek to the requested sector");

    ExpectThrows<ArgumentOutOfRangeException>(() => DlLevelInfoReader.ReadSectorHeader(stream, levelSet.RequestedLevel.LevelWad, 0));
    ExpectThrows<InvalidDataException>(() => DlLevelInfoReader.ReadSectorHeader(stream, new DlFileBlock(int.MaxValue, 1), 1));
    ExpectThrows<ArgumentOutOfRangeException>(() => DlLevelInfoReader.ReadLevelSet(stream, DlLevelConstants.LevelInfoCount));
}

static void ValidateLevelWadParsing()
{
    var levelWadBytes = new byte[DlLevelConstants.SectorSize * 5];
    WriteInt32(levelWadBytes, 0x00, DlLevelConstants.LevelWadHeaderSize);
    WriteInt32(levelWadBytes, 0x04, 0x1234);
    WriteInt32(levelWadBytes, 0x08, 7);
    WriteInt32(levelWadBytes, 0x0c, 2);
    WriteInt32(levelWadBytes, 0x10, 0x1111);
    WriteInt32(levelWadBytes, 0x14, 0x2222);
    WriteFileBlock(levelWadBytes, 0x18, new DlFileBlock(2, 1));
    WriteFileBlock(levelWadBytes, 0x20, new DlFileBlock(3, 1));
    WriteFileBlock(levelWadBytes, 0x28, new DlFileBlock(4, 1));
    levelWadBytes[2 * DlLevelConstants.SectorSize] = 0xaa;
    levelWadBytes[3 * DlLevelConstants.SectorSize] = 0xbb;

    var levelWad = DlLevelWadReader.ReadLevelWad(levelWadBytes);
    Expect(levelWad.HeaderSize == DlLevelConstants.LevelWadHeaderSize, "level WAD header size should be parsed");
    Expect(levelWad.Sector == 0x1234, "level WAD sector should be parsed");
    Expect(levelWad.Level == 7, "level WAD level id should be parsed");
    Expect(levelWad.Data == new DlFileBlock(2, 1), "core level fileblock should be parsed");
    Expect(levelWad.CoreBank == new DlFileBlock(3, 1), "core bank fileblock should be parsed");
    Expect(levelWad.Chunks[0] == new DlFileBlock(4, 1), "first chunk fileblock should be parsed");
    Expect(levelWad.HeaderBytes.Length == DlLevelConstants.LevelWadHeaderSize, "level WAD header bytes should be preserved");

    var coreLevel = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.Data);
    Expect(coreLevel.Length == DlLevelConstants.SectorSize, "sector fileblock should read sector-scaled length");
    Expect(coreLevel[0] == 0xaa, "sector fileblock should read from the requested sector");

    var byteLengthBlock = DlLevelWadReader.ReadByteLengthFileBlock(levelWadBytes, new DlFileBlock(3, 17));
    Expect(byteLengthBlock.Length == 17, "byte-length fileblock should not sector-scale length");
    Expect(byteLengthBlock[0] == 0xbb, "byte-length fileblock should seek by sector offset");

    var isoBytes = new byte[DlLevelConstants.SectorSize * 8];
    isoBytes[(4 + 2) * DlLevelConstants.SectorSize] = 0xcc;
    isoBytes[(4 + 3) * DlLevelConstants.SectorSize] = 0xdd;
    using var isoStream = new MemoryStream(isoBytes, writable: false);

    var relativeSectorBlock = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, 4, new DlFileBlock(2, 1));
    Expect(relativeSectorBlock.Length == DlLevelConstants.SectorSize, "relative sector fileblock should read sector-scaled length");
    Expect(relativeSectorBlock[0] == 0xcc, "relative sector fileblock should add the WAD base sector");

    var relativeByteBlock = DlLevelInfoReader.ReadByteLengthSectorRelativeBlock(isoStream, 4, new DlFileBlock(3, 17));
    Expect(relativeByteBlock.Length == 17, "relative byte-length fileblock should not sector-scale length");
    Expect(relativeByteBlock[0] == 0xdd, "relative byte-length fileblock should add the WAD base sector");
}

static void ValidateLooseLevelWadExtraction()
{
    const int levelIndex = 3;
    const int headerSector = 20;
    const int payloadBaseSector = 60;
    var looseWadBytes = CreateSyntheticLooseLevelWad(payloadBaseSector);
    var iso = CreateSyntheticIso(levelIndex, headerSector, payloadBaseSector, looseWadBytes);

    using var stream = new MemoryStream(iso, writable: false);
    var extracted = DlLooseLevelWadExtractor.ExtractPrimary(stream, levelIndex);

    Expect(extracted.LevelIndex == levelIndex, "loose WAD extraction should preserve requested level index");
    Expect(extracted.HeaderSector == headerSector, "loose WAD extraction should report the header sector");
    Expect(extracted.PayloadBaseSector == payloadBaseSector, "loose WAD extraction should report the payload base sector");
    Expect(extracted.SectorCount == looseWadBytes.Length / DlLevelConstants.SectorSize, "loose WAD extraction should copy through the last referenced sector");
    Expect(extracted.Bytes.SequenceEqual(looseWadBytes), "loose WAD extraction should preserve referenced WAD bytes in a self-contained layout");
}

static void ValidateLooseLevelWadUnpacking()
{
    var looseWadBytes = CreateSyntheticLooseLevelWad(payloadBaseSector: 20);
    var package = DlLevelWadUnpacker.Unpack(looseWadBytes);
    var files = package.Files.ToDictionary(file => file.Path);

    Expect(files.ContainsKey("level_wad/header.bin"), "loose WAD unpack should include the level WAD header");
    Expect(files["level_wad/core_sound.bnk"].Bytes[0] == 0x41, "loose WAD unpack should include core sound bank bytes");
    Expect(files["level_wad/chunks/chunk0.wad"].Bytes[0] == 0x51, "loose WAD unpack should include chunk bytes");
    Expect(files["missions/0000/mission.wad"].Bytes[0x60] == 0xA1, "loose WAD unpack should include mission WAD bytes");
    Expect(files["missions/0000/gameplay.bin"].Bytes[0x20] == 0xA1, "loose WAD unpack should slice mission gameplay bytes");
    Expect(DlMissionDataReader.ReadGameplay(files["missions/0000/mission.wad"].Bytes)[0x20] == 0xA1, "mission gameplay reader should expose the payload for render packages");
    Expect(files["missions/0000/gameplay/moby_classes.bin"].Bytes.SequenceEqual(new byte[] { 0xA1, 0xA2 }), "loose WAD unpack should split mission gameplay moby classes");
    Expect(files["missions/0000/gameplay/moby_instances.bin"].Bytes.SequenceEqual(new byte[] { 0xA3, 0xA4 }), "loose WAD unpack should split mission gameplay moby instances");
    Expect(files["missions/0000/classes.bin"].Bytes.SequenceEqual(new byte[] { 0xB1, 0xB2, 0xB3, 0xB4 }), "loose WAD unpack should slice mission classes bytes");
    Expect(files["missions/0000/gameplay_instances.bin"].Bytes[0] == 0x81, "loose WAD unpack should include mission instance bytes");
    Expect(!files.ContainsKey("missions/0001/mission.wad"), "loose WAD unpack should skip placeholder missions");
    Expect(files["assets/asset_header.bin"].Bytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "loose WAD unpack should expose core asset header payload");
    Expect(files["gameplay/core/level_settings.bin"].Bytes.SequenceEqual(new byte[] { 0xC1, 0xC2, 0xC3 }), "loose WAD unpack should split core gameplay level settings");
    Expect(files["gameplay/core/cameras.bin"].Bytes.SequenceEqual(new byte[] { 0xD1, 0xD2 }), "loose WAD unpack should split core gameplay cameras");
    Expect(files["world/lighting/directional_lights.bin"].Bytes[0] == 0xD1, "loose WAD unpack should expose parsed world slot payloads");

    var packed = package.ToPackedPackage();
    Expect(packed.Entries.Count == package.Files.Count, "packed package entry count should match loose file count");
    var packedMissionEntry = packed.Entries.Single(entry => entry.Path == "missions/0000/mission.wad");
    var packedMissionBytes = packed.PackedBytes.AsSpan(packedMissionEntry.Offset, packedMissionEntry.Length).ToArray();
    Expect(packedMissionBytes.SequenceEqual(files["missions/0000/mission.wad"].Bytes), "packed package offsets should round-trip entry bytes");
}

static void ValidateUyaLevelInfoLookup()
{
    var iso = new byte[Math.Max(
        UyaLevelConstants.RetailLevelInfoTableOffset
            + (UyaLevelConstants.LevelInfoCount * UyaLevelConstants.LevelInfoSize),
        UyaLevelConstants.SectorSize * 40)];

    WriteUyaLevelInfoEntry(
        iso,
        3,
        audio: new UyaFileBlock(30, 1),
        level: new UyaFileBlock(31, 1),
        scene: new UyaFileBlock(32, 2));

    iso[31 * UyaLevelConstants.SectorSize] = 0x42;

    using var stream = new MemoryStream(iso, writable: false);
    var levelSet = UyaLevelInfoReader.ReadLevelSet(stream, 3);

    Expect(levelSet.RequestedLevelIndex == 3, "UYA requested level index should be preserved");
    Expect(levelSet.RequestedLevel.LevelAudioWad == new UyaFileBlock(30, 1), "UYA audio WAD block should be parsed");
    Expect(levelSet.RequestedLevel.LevelWad == new UyaFileBlock(31, 1), "UYA level WAD block should be parsed");
    Expect(levelSet.RequestedLevel.LevelSceneWad == new UyaFileBlock(32, 2), "UYA scene WAD block should be parsed");

    stream.Position = 0;
    var levelWadBytes = UyaLevelInfoReader.ReadSectorBlock(stream, levelSet.RequestedLevel.LevelWad);
    Expect(levelWadBytes.Length == UyaLevelConstants.SectorSize, "UYA sector block read should return sector-scaled length");
    Expect(levelWadBytes[0] == 0x42, "UYA sector block read should seek to the requested sector");

    stream.Position = 0;
    var levelWadHeaderBytes = UyaLevelInfoReader.ReadSectorHeader(stream, levelSet.RequestedLevel.LevelWad, 1);
    Expect(levelWadHeaderBytes.Length == UyaLevelConstants.SectorSize, "UYA fixed sector header read should ignore fileblock length");
    Expect(levelWadHeaderBytes[0] == 0x42, "UYA fixed sector header read should seek to the requested sector");

    ExpectThrows<ArgumentOutOfRangeException>(() => UyaLevelInfoReader.ReadSectorHeader(stream, levelSet.RequestedLevel.LevelWad, 0));
    ExpectThrows<InvalidDataException>(() => UyaLevelInfoReader.ReadSectorHeader(stream, new UyaFileBlock(int.MaxValue, 1), 1));
    ExpectThrows<ArgumentOutOfRangeException>(() => UyaLevelInfoReader.ReadLevelSet(stream, UyaLevelConstants.LevelInfoCount));
}

static void ValidateGcLevelInfoLookup()
{
    var iso = new byte[GcLevelCatalog.RetailLevelInfoTableOffset
        + ((GcLevelCatalog.Levels.Max(level => level.TableIndex) + 1) * GcLevelCatalog.LevelInfoSize)];
    var museum = GcLevelCatalog.GetById(30);
    var offset = GcLevelCatalog.RetailLevelInfoTableOffset + (museum.TableIndex * GcLevelCatalog.LevelInfoSize);
    WriteInt32(iso, offset + 0x00, 31);
    WriteInt32(iso, offset + 0x04, 2);
    WriteInt32(iso, offset + 0x08, 32);
    WriteInt32(iso, offset + 0x0c, 3);
    WriteInt32(iso, offset + 0x10, 33);
    WriteInt32(iso, offset + 0x14, 4);

    using var stream = new MemoryStream(iso, writable: false);
    var levelInfo = GcLevelInfoReader.ReadLevel(stream, 30);

    Expect(GcLevelCatalog.Levels.Count == 27, "GC level catalog should include every Wrench level");
    Expect(levelInfo.Level.TableIndex == 21, "GC Museum level id 30 should map to table index 21");
    Expect(levelInfo.LevelWad == new GcFileBlock(31, 2), "GC level WAD should be the first table block");
    Expect(levelInfo.LevelAudioWad == new GcFileBlock(32, 3), "GC audio WAD should be the second table block");
    Expect(levelInfo.LevelSceneWad == new GcFileBlock(33, 4), "GC scene WAD should be the third table block");
    ExpectThrows<ArgumentOutOfRangeException>(() => GcLevelCatalog.GetById(21));
}

static void ValidateUyaLevelWadParsing()
{
    var levelWadBytes = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    var levelWad = UyaLevelWadReader.ReadLevelWad(levelWadBytes);

    Expect(levelWad.HeaderSize == UyaLevelConstants.LevelWadHeaderSize, "UYA level WAD header size should be parsed");
    Expect(levelWad.Sector == 0x1234, "UYA level WAD sector should be parsed");
    Expect(levelWad.Level == 7, "UYA level WAD level id should be parsed");
    Expect(levelWad.ReverbType == 2, "UYA level WAD reverb should be parsed");
    Expect(levelWad.Data == new UyaFileBlock(3, 2), "UYA level data fileblock should be parsed");
    Expect(levelWad.SoundBank == new UyaFileBlock(1, 1), "UYA sound bank fileblock should be parsed");
    Expect(levelWad.Gameplay == new UyaFileBlock(5, 1), "UYA gameplay fileblock should be parsed");
    Expect(levelWad.Occlusion == new UyaFileBlock(6, 1), "UYA occlusion fileblock should be parsed");
    Expect(levelWad.Chunks[0] == new UyaFileBlock(7, 1), "UYA first chunk fileblock should be parsed");
    Expect(levelWad.ChunkBanks[0] == new UyaFileBlock(8, 1), "UYA first chunk bank fileblock should be parsed");
    Expect(levelWad.HeaderBytes.Length == UyaLevelConstants.LevelWadHeaderSize, "UYA level WAD header bytes should be preserved");

    var soundBank = UyaLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.SoundBank);
    Expect(soundBank.Length == UyaLevelConstants.SectorSize, "UYA sector fileblock should read sector-scaled length");
    Expect(soundBank[0] == 0x41, "UYA sector fileblock should read from the requested sector");

    var levelData = UyaLevelWadReader.ReadLevelDataWad(UyaLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.Data));
    Expect(levelData.HeaderSize == UyaLevelConstants.LevelDataHeaderSize, "UYA level data fixed header size should be reported");
    Expect(levelData.Overlay == new UyaByteBlock(0x80, 4), "UYA level data overlay byte block should be parsed");
    Expect(levelData.CoreIndex == new UyaByteBlock(0x90, 4), "UYA level data core index byte block should be parsed");
    Expect(levelData.GsRam == new UyaByteBlock(0xa0, 4), "UYA level data GS RAM byte block should be parsed");
    Expect(levelData.HudHeader == new UyaByteBlock(0xb0, 4), "UYA level data HUD header byte block should be parsed");
    Expect(levelData.HudBanks[0] == new UyaByteBlock(0xc0, 4), "UYA level data first HUD bank byte block should be parsed");
    Expect(levelData.CoreData == new UyaByteBlock(0xd0, 4), "UYA level data core payload byte block should be parsed");
    Expect(levelData.TransitionTextures == new UyaByteBlock(0xe0, 4), "UYA level data transition texture byte block should be parsed");

    var code = UyaLevelWadReader.ReadByteFileBlock(UyaLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.Data), levelData.Overlay);
    Expect(code.SequenceEqual(new byte[] { 0x11, 0x12, 0x13, 0x14 }), "UYA byte fileblock should read exact byte length");
}

static void ValidateUyaLevelWadInventory()
{
    var bytes = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    bytes[0x100] = 0xE1;
    bytes[(3 * UyaLevelConstants.SectorSize) + 0x60] = 0xE2;
    var inventory = UyaLevelWadInventoryReader.Read(bytes);
    var root = inventory.Containers.Single(container => container.Path == "level_wad");
    var levelData = inventory.Containers.Single(container => container.Path == "level_wad/level_data.wad");
    var gameplay = inventory.Containers.Single(container => container.Path == "gameplay/gameplay_core.bin");
    var assets = inventory.Containers.Single(container => container.Path == "assets/asset_wad.bin");

    Expect(root.Slots.Count == 10, "UYA inventory should retain every declared outer slot");
    Expect(root.Slots.Single(slot => slot.LogicalPaths.Contains("level_wad/chunks/chunk1.wad")).Length == 0,
        "UYA inventory should retain empty chunk slots");
    Expect(levelData.Slots.Count == 11, "UYA inventory should retain every level-data slot");
    Expect(MemoryMarshal.TryGetArray(levelData.Slots.Single(slot => slot.Path == "code/code.bin").Bytes, out var codeSegment)
        && ReferenceEquals(codeSegment.Array, bytes),
        "UYA inventory should expose nested payloads as slices of the source buffer");
    Expect(gameplay.Slots.Any(slot => slot.LogicalPaths.Contains("gameplay/core/cameras.bin") && slot.Length == 0),
        "UYA inventory should retain empty gameplay slots");
    Expect(assets.Regions.Single().Kind == UyaContainerRegionKind.Payload,
        "UYA inventory should retain undecoded asset data as an owned payload");
    Expect(root.Regions.Any(region => region.Kind == UyaContainerRegionKind.Padding),
        "UYA inventory should identify the sector-aligned outer header padding");
    Expect(root.Regions.Any(region => region.Kind == UyaContainerRegionKind.Opaque),
        "UYA inventory should retain unclaimed outer bytes as opaque regions");
    foreach (var container in inventory.Containers) ExpectCompleteCoverage(container);
    Expect(UyaLevelWadWriter.Write(inventory).SequenceEqual(bytes),
        "unchanged UYA level WAD writes should be byte-identical");
    foreach (var container in inventory.Containers)
        Expect(UyaLevelWadWriter.WriteContainer(container).AsSpan().SequenceEqual(container.Bytes.Span),
            $"unchanged {container.Path} writes should be byte-identical");

    var sourceSnapshot = bytes.ToArray();
    var replacementCode = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
    var rewritten = UyaLevelWadWriter.Write(inventory, new Dictionary<string, ReadOnlyMemory<byte>>
    {
        ["code/code.bin"] = replacementCode,
    });
    var rewrittenInventory = UyaLevelWadInventoryReader.Read(rewritten);
    var rewrittenRoot = rewrittenInventory.Containers.Single(container => container.Path == "level_wad");
    var rewrittenLevelData = rewrittenInventory.Containers.Single(container => container.Path == "level_wad/level_data.wad");
    Expect(rewrittenLevelData.Slots.Single(slot => slot.Path == "code/code.bin").Bytes.Span.SequenceEqual(replacementCode),
        "UYA nested replacement bytes should survive a writer/readback cycle");
    Expect(rewrittenLevelData.Slots.Single(slot => slot.Path == "assets/asset_header.bin").Offset > 0x90,
        "UYA nested slots after a growing replacement should be relocated");
    Expect(rewrittenRoot.Slots.Single(slot => slot.Path == "level_wad/level_data.wad").Length == 3 * UyaLevelConstants.SectorSize,
        "UYA outer fileblock lengths should be recalculated in sectors");
    Expect(rewrittenRoot.Slots.Single(slot => slot.Path == "gameplay/gameplay.bin").Offset == 6 * UyaLevelConstants.SectorSize,
        "UYA outer fileblocks after a growing replacement should be relocated");
    Expect(rewrittenRoot.Regions.Single(region => region.Kind == UyaContainerRegionKind.Padding).Bytes.Span.Contains((byte)0xE1)
        && rewrittenLevelData.Regions.Any(region => region.Kind == UyaContainerRegionKind.Opaque
            && region.Bytes.Span.Contains((byte)0xE2)),
        "UYA writes should preserve padding and opaque gap bytes while relocating payloads");
    Expect(bytes.SequenceEqual(sourceSnapshot), "UYA writes should not mutate source memory");

    var replacementMobyInstances = Enumerable.Repeat((byte)0xAC, 17).ToArray();
    var gameplayRewrite = UyaLevelWadWriter.Write(inventory, new Dictionary<string, ReadOnlyMemory<byte>>
    {
        ["gameplay/core/moby_instances.bin"] = replacementMobyInstances,
    });
    var gameplayRewriteInventory = UyaLevelWadInventoryReader.Read(gameplayRewrite);
    var rewrittenMobyInstances = gameplayRewriteInventory.Containers
        .Single(container => container.Path == "gameplay/gameplay_core.bin").Slots
        .Single(slot => slot.LogicalPaths.Contains("gameplay/core/moby_instances.bin"));
    Expect(rewrittenMobyInstances.Bytes.Length >= replacementMobyInstances.Length
        && rewrittenMobyInstances.Bytes.Span[..replacementMobyInstances.Length].SequenceEqual(replacementMobyInstances)
        && rewrittenMobyInstances.Bytes.Span[replacementMobyInstances.Length..].ContainsAnyExcept((byte)0) == false,
        "UYA gameplay pointer tables should be recalculated after replacement");
    Expect(gameplayRewriteInventory.Containers.Single(container => container.Path == "gameplay/gameplay_core.bin")
        .Slots.Where(slot => slot.Length > 0).All(slot => slot.Offset % slot.Alignment == 0),
        "UYA gameplay replacements should preserve native pointer alignment");
    ExpectThrows<ArgumentException>(() => UyaLevelWadWriter.Write(inventory,
        new Dictionary<string, ReadOnlyMemory<byte>> { ["unknown.bin"] = ReadOnlyMemory<byte>.Empty }));
    ExpectThrows<ArgumentException>(() => UyaLevelWadWriter.Write(inventory,
        new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["level_wad/level_data.wad"] = ReadOnlyMemory<byte>.Empty,
            ["code/code.bin"] = ReadOnlyMemory<byte>.Empty,
        }));
    ExpectThrows<InvalidDataException>(() => UyaLevelWadWriter.WriteContainer(
        root with { Regions = root.Regions.Skip(1).ToArray() }));
    var tamperedSource = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    var tamperedInventory = UyaLevelWadInventoryReader.Read(tamperedSource);
    tamperedSource[UyaLevelConstants.SectorSize] ^= 0xFF;
    ExpectThrows<InvalidDataException>(() => UyaLevelWadWriter.Write(tamperedInventory));

    var compressedBytes = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    var uncompressedGameplay = CreateSyntheticUyaGameplay();
    Array.Resize(ref uncompressedGameplay, uncompressedGameplay.Length + 16);
    var compressedGameplay = WadCompression.Compress(uncompressedGameplay);
    Expect(compressedGameplay.Length <= UyaLevelConstants.SectorSize,
        "synthetic compressed gameplay should fit its declared sector");
    compressedBytes.AsSpan(5 * UyaLevelConstants.SectorSize, UyaLevelConstants.SectorSize).Clear();
    compressedGameplay.CopyTo(compressedBytes.AsSpan(5 * UyaLevelConstants.SectorSize));
    var compressedInventory = UyaLevelWadInventoryReader.Read(compressedBytes);
    var decodedGameplay = compressedInventory.Containers.Single(container => container.Path == "gameplay/gameplay_core.bin");
    Expect(decodedGameplay.SourceCompression == UyaContainerCompression.Wad,
        "UYA inventory should retain compressed gameplay provenance");
    Expect(decodedGameplay.Bytes.Span.SequenceEqual(uncompressedGameplay),
        "UYA inventory should expose the exact decompressed gameplay image");
    Expect(UyaLevelWadWriter.Write(compressedInventory).SequenceEqual(compressedBytes),
        "unchanged UYA writes should retain original compressed child payloads");
    Expect(UyaLevelWadWriter.WriteContainer(decodedGameplay).AsSpan().SequenceEqual(uncompressedGameplay),
        "unchanged decoded UYA container writes should reproduce decompressed source bytes");

    var overlap = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    WriteUyaFileBlock(overlap, 0x20, new UyaFileBlock(5, 2));
    ExpectThrows<InvalidDataException>(() => UyaLevelWadInventoryReader.Read(overlap));

    var nestedOverlap = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    WriteByteBlock(nestedOverlap, (3 * UyaLevelConstants.SectorSize) + 0x08, new UyaByteBlock(0x82, 4));
    ExpectThrows<InvalidDataException>(() => UyaLevelWadInventoryReader.Read(nestedOverlap));

    var emptySentinel = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    WriteByteBlock(emptySentinel, (3 * UyaLevelConstants.SectorSize) + 0x50, new UyaByteBlock(-1, 0));
    var transitionTextures = UyaLevelWadInventoryReader.Read(emptySentinel).Containers
        .Single(container => container.Path == "level_wad/level_data.wad").Slots
        .Single(slot => slot.Path == "transition_textures/transition_textures.bin");
    Expect(transitionTextures is { DeclaredOffset: -1, DeclaredLength: 0, Offset: 0, Length: 0 },
        "UYA inventory should preserve empty negative sentinels without treating them as byte ranges");

    var overflow = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    WriteUyaFileBlock(overflow, 0x10, new UyaFileBlock(int.MaxValue, 1));
    ExpectThrows<InvalidDataException>(() => UyaLevelWadInventoryReader.Read(overflow));
}

static void ExpectCompleteCoverage(UyaContainerInventory container)
{
    var cursor = 0;
    foreach (var region in container.Regions)
    {
        Expect(region.Offset == cursor, $"{container.Path} inventory should not contain gaps or overlaps");
        cursor = checked(cursor + region.Length);
    }
    Expect(cursor == container.Bytes.Length, $"{container.Path} inventory should own every byte");
}

static void ValidateUyaLevelWadInventoryWhenAvailable()
{
    var directory = Path.Combine("test-assets", "extractions_uya");
    if (!Directory.Exists(directory)) return;
    foreach (var path in Directory.EnumerateFiles(directory, "level*.wad", SearchOption.TopDirectoryOnly)
        .Order(StringComparer.Ordinal))
    {
        var inventory = UyaLevelWadInventoryReader.Read(File.ReadAllBytes(path));
        foreach (var container in inventory.Containers) ExpectCompleteCoverage(container);
        Expect(UyaLevelWadWriter.Write(inventory).AsSpan().SequenceEqual(inventory.Containers[0].Bytes.Span),
            $"{Path.GetFileName(path)} should round-trip byte-for-byte");
        foreach (var container in inventory.Containers)
            Expect(UyaLevelWadWriter.WriteContainer(container).AsSpan().SequenceEqual(container.Bytes.Span),
                $"{Path.GetFileName(path)} {container.Path} should round-trip byte-for-byte");
    }
}

static void ValidateUyaLevelArchiveBuilder()
{
    var capability = LevelArchiveBuilder.GetCapability(GameId.UYA);
    Expect(LevelArchiveBuilder.SupportsTarget(
            capability.Game, capability.Region, capability.Revisions.Single(), capability.BakeProfile),
        "UYA SDK archive capability should recognize its declared target");
    Expect(!LevelArchiveBuilder.SupportsTarget("UYA", "PAL", "1.00", "uya-ntsc-u"),
        "UYA SDK archive capability should reject undeclared targets");

    var source = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    var gameplay = CreateSyntheticUyaGameplay();
    var compressedGameplay = WadCompression.CompressVerified(gameplay).CompressedBytes;
    Expect(compressedGameplay.Length <= UyaLevelConstants.SectorSize,
        "synthetic gameplay should fit its outer sector slot");
    source.AsSpan(5 * UyaLevelConstants.SectorSize, UyaLevelConstants.SectorSize).Clear();
    compressedGameplay.CopyTo(source.AsSpan(5 * UyaLevelConstants.SectorSize));

    var phases = new List<LevelArchivePhase>();
    var progress = new InlineProgress<LevelArchiveProgress>(value => phases.Add(value.Phase));
    var memoryResult = LevelArchiveBuilder.Build(GameId.UYA,
        source,
        options: new() { RequireSourceEquality = true },
        progress: progress);
    Expect(memoryResult.Succeeded && memoryResult.OutputBytes is not null,
        "UYA SDK archive memory workflow should succeed");
    Expect(memoryResult.OutputBytes.SequenceEqual(source),
        "unchanged deterministic UYA SDK archive output should match its synthetic source");
    Expect(memoryResult.SourceSha256 == memoryResult.UncompressedSha256
        && memoryResult.SourceSha256 == memoryResult.CompressedSha256,
        "unchanged UYA SDK archive hashes should agree");
    Expect(memoryResult.Compressions.Any(item => item.Path == "gameplay/gameplay_core.bin"),
        "UYA SDK archive workflow should report nested gameplay compression");
    Expect(phases.SequenceEqual([
        LevelArchivePhase.Inventory,
        LevelArchivePhase.Rebuild,
        LevelArchivePhase.Validate,
        LevelArchivePhase.Compress,
        LevelArchivePhase.Complete]),
        "UYA SDK archive memory progress should report ordered phases");

    using var stream = new MemoryStream(source, writable: false);
    var streamPhases = new List<LevelArchivePhase>();
    var streamResult = LevelArchiveBuilder.Build(GameId.UYA,
        stream,
        options: new() { RequireSourceEquality = true },
        progress: new InlineProgress<LevelArchiveProgress>(value => streamPhases.Add(value.Phase)));
    Expect(streamResult.Succeeded && streamResult.OutputBytes!.SequenceEqual(memoryResult.OutputBytes),
        "UYA SDK archive stream and memory entry points should agree");
    Expect(streamPhases[0] == LevelArchivePhase.Reading
        && streamPhases.Skip(1).SequenceEqual(phases),
        "UYA SDK archive stream progress should add only the reading phase");

    var replacementMobyInstances = Enumerable.Repeat((byte)0xBC, 40).ToArray();
    var changed = LevelArchiveBuilder.Build(GameId.UYA, source, new Dictionary<string, ReadOnlyMemory<byte>>
    {
        ["gameplay/core/moby_instances.bin"] = replacementMobyInstances,
    });
    Expect(changed.Succeeded && changed.OutputBytes is not null,
        "UYA SDK archive workflow should accept nested replacements");
    var changedInventory = UyaLevelWadInventoryReader.Read(changed.OutputBytes);
    var changedGameplay = changedInventory.Containers.Single(container => container.Path == "gameplay/gameplay_core.bin");
    Expect(changedGameplay.Slots.Single(slot =>
            slot.LogicalPaths.Contains("gameplay/core/moby_instances.bin")).Bytes.Span.SequenceEqual(replacementMobyInstances),
        "UYA SDK archive replacements should survive final compression and reader re-entry");
    Expect(changedInventory.Containers.Single(container => container.Path == "level_wad").Slots
            .Single(slot => slot.Path == "gameplay/gameplay.bin").Compression == UyaContainerCompression.Wad,
        "UYA SDK archive workflow should publish gameplay as a verified compressed WAD");
    Expect(changed.ChangedRegions.Any(change => change.Path == "gameplay/core/moby_instances.bin")
        && changed.SourceSha256 != changed.UncompressedSha256
        && changed.UncompressedSha256 != changed.CompressedSha256,
        "UYA SDK archive results should report changed regions and distinct build-stage hashes");

    var sourceWithUntouchedWad = source.ToArray();
    var soundSlot = sourceWithUntouchedWad.AsSpan(UyaLevelConstants.SectorSize, UyaLevelConstants.SectorSize);
    soundSlot.Clear();
    CreateLiteralWad([0x51, 0x52, 0x53, 0x54]).CopyTo(soundSlot);
    var changedWithUntouchedWad = LevelArchiveBuilder.Build(GameId.UYA,
        sourceWithUntouchedWad,
        new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["gameplay/core/moby_instances.bin"] = replacementMobyInstances,
        });
    var untouchedSound = UyaLevelWadInventoryReader.Read(changedWithUntouchedWad.OutputBytes!).Containers
        .Single(container => container.Path == "level_wad").Slots
        .Single(slot => slot.Path == "level_wad/sound.bnk");
    Expect(untouchedSound.Bytes.Span.SequenceEqual(soundSlot),
        "UYA SDK archive builds should preserve unrelated compressed payloads byte-for-byte");

    var equalityFailure = LevelArchiveBuilder.Build(GameId.UYA,
        source,
        new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["gameplay/core/moby_instances.bin"] = replacementMobyInstances,
        },
        new() { RequireSourceEquality = true });
    Expect(!equalityFailure.Succeeded && equalityFailure.OutputBytes is null
        && equalityFailure.Diagnostics.Any(diagnostic => diagnostic.Blocking),
        "UYA SDK archive equality failures should return no partial output and a blocking diagnostic");
    var parentReplacementFailure = LevelArchiveBuilder.Build(GameId.UYA,
        source,
        new Dictionary<string, ReadOnlyMemory<byte>> { ["gameplay/gameplay.bin"] = gameplay });
    Expect(!parentReplacementFailure.Succeeded && parentReplacementFailure.OutputBytes is null,
        "UYA SDK archive workflow should reject ambiguous encoded parent replacements");
    var malformed = LevelArchiveBuilder.Build(GameId.UYA, new byte[8]);
    Expect(!malformed.Succeeded && malformed.OutputBytes is null
        && malformed.Diagnostics.Single().Code == "UYA_ARCHIVE_BUILD_FAILED",
        "malformed UYA SDK archive input should return a blocking diagnostic without output");

    foreach (var phase in new[]
        {
            LevelArchivePhase.Inventory,
            LevelArchivePhase.Rebuild,
            LevelArchivePhase.Validate,
            LevelArchivePhase.Compress,
            LevelArchivePhase.Complete,
        })
    {
        using var cancellation = new CancellationTokenSource();
        var cancelingProgress = new InlineProgress<LevelArchiveProgress>(value =>
        {
            if (value.Phase == phase) cancellation.Cancel();
        });
        ExpectThrows<OperationCanceledException>(() => LevelArchiveBuilder.Build(GameId.UYA,
            source, progress: cancelingProgress, cancellationToken: cancellation.Token));
    }
    using (var cancellation = new CancellationTokenSource())
    using (var cancellationStream = new MemoryStream(source, writable: false))
    {
        var cancelingProgress = new InlineProgress<LevelArchiveProgress>(value =>
        {
            if (value.Phase == LevelArchivePhase.Reading) cancellation.Cancel();
        });
        ExpectThrows<OperationCanceledException>(() => LevelArchiveBuilder.Build(GameId.UYA,
            cancellationStream, progress: cancelingProgress, cancellationToken: cancellation.Token));
    }
}

static void ValidateUyaLevelAssetComposer()
{
    var header = new byte[0x120];
    WriteInt32(header, 0x08, 0x20);
    WriteInt32(header, 0x10, 0x40);
    WriteInt32(header, 0x14, 0x60);
    WriteInt32(header, 0x18, 1);
    WriteInt32(header, 0x1c, 0xc0);
    WriteInt32(header, 0xc0, 0x80);
    var assets = new byte[0xa0];
    assets.AsSpan(0x20, 0x20).Fill(0x11);
    assets.AsSpan(0x40, 0x20).Fill(0x22);
    assets.AsSpan(0x60, 0x20).Fill(0x33);
    assets.AsSpan(0x80, 0x20).Fill(0x44);
    var terrain = Enumerable.Repeat((byte)0xaa, 0x31).ToArray();
    var collision = Enumerable.Repeat((byte)0xcc, 0x11).ToArray();

    var composed = LevelAssetComposer.ComposeAssetWad(
        GameId.UYA, header, assets, new(Terrain: terrain, Collision: collision));
    var composedHeader = DlAssetReader.ReadHeader(composed.HeaderBytes);
    var moby = DlAssetReader.ReadModelDefinitions(
        composed.HeaderBytes, composedHeader.MobyModelOffset, composedHeader.MobyModelCount).Single();
    Expect(composedHeader.TerrainOffset == 0x20
        && composedHeader.SkyOffset == 0x60
        && composedHeader.CollisionOffset == 0x80
        && moby.ModelOffset == 0xa0,
        "UYA asset composer should relocate every pointer after resized payloads");
    Expect(composed.AssetWadBytes.AsSpan(composedHeader.TerrainOffset, terrain.Length).SequenceEqual(terrain)
        && composed.AssetWadBytes.AsSpan(composedHeader.SkyOffset, 0x20)
            .SequenceEqual(Enumerable.Repeat((byte)0x22, 0x20).ToArray())
        && composed.AssetWadBytes.AsSpan(composedHeader.CollisionOffset, collision.Length).SequenceEqual(collision)
        && composed.AssetWadBytes.AsSpan(moby.ModelOffset, 0x20)
            .SequenceEqual(Enumerable.Repeat((byte)0x44, 0x20).ToArray()),
        "UYA asset composer should preserve untouched payloads around replacements");
    var unchanged = LevelAssetComposer.ComposeAssetWad(GameId.UYA, header, assets, new());
    Expect(unchanged.HeaderBytes.SequenceEqual(header) && unchanged.AssetWadBytes.SequenceEqual(assets),
        "UYA asset composer no-op should remain byte-identical");

    var sourceTerrain = Enumerable.Repeat((byte)0x5a, 0x180).ToArray();
    var encodedTerrain = WadCompression.CompressVerified(sourceTerrain).CompressedBytes;
    var sourceEnd = (0x10 + encodedTerrain.Length + 0x0f) & ~0x0f;
    var chunk = new byte[sourceEnd + 0x20];
    WriteInt32(chunk, 0x00, 0x10);
    WriteInt32(chunk, 0x04, sourceEnd);
    WriteInt32(chunk, 0x08, sourceEnd + 0x10);
    encodedTerrain.CopyTo(chunk.AsSpan(0x10));
    chunk.AsSpan(sourceEnd, 0x20).Fill(0x7b);
    var replacementTerrain = Enumerable.Repeat((byte)0xa5, 0x281).ToArray();
    var composedChunk = LevelAssetComposer.ComposeTfragChunk(GameId.UYA, chunk, replacementTerrain);
    var movedSuffix = BinaryPrimitives.ReadInt32LittleEndian(composedChunk.AsSpan(0x04));
    Expect(TfragChunkWadReader.ReadTerrainPayload(composedChunk).SequenceEqual(replacementTerrain)
        && composedChunk.AsSpan(movedSuffix, 0x20).SequenceEqual(chunk.AsSpan(sourceEnd, 0x20)),
        "UYA chunk composer should replace compressed terrain and preserve its trailing payloads");

    var plainChunk = new byte[0x28];
    WriteInt32(plainChunk, 0x00, 0x10);
    WriteInt32(plainChunk, 0x04, 0x20);
    plainChunk.AsSpan(0x10, 0x10).Fill(0x12);
    plainChunk.AsSpan(0x20, 8).Fill(0x34);
    var plainTerrain = Enumerable.Repeat((byte)0x56, 7).ToArray();
    var composedPlainChunk = LevelAssetComposer.ComposeTfragChunk(GameId.UYA, plainChunk, plainTerrain);
    Expect(TfragChunkWadReader.ReadTerrainPayload(composedPlainChunk).SequenceEqual(plainTerrain)
        && BinaryPrimitives.ReadInt32LittleEndian(composedPlainChunk.AsSpan(0x04)) == 0x17
        && composedPlainChunk.AsSpan(0x17, 8).SequenceEqual(plainChunk.AsSpan(0x20, 8)),
        "UYA chunk composer should not expose alignment padding as uncompressed terrain");
}

static void ValidateIsoPatchPlanning()
{
    const int levelIndex = 3;
    const int headerSector = 20;
    const int payloadBaseSector = 60;
    var source = CreateSyntheticUyaLooseLevelWad(payloadBaseSector);
    WriteInt32(source, 0x08, levelIndex);
    var iso = CreateSyntheticUyaIso(levelIndex, headerSector, payloadBaseSector, source);
    Array.Resize(ref iso, (iso.Length + UyaLevelConstants.SectorSize - 1)
        / UyaLevelConstants.SectorSize * UyaLevelConstants.SectorSize);
    WriteUyaLevelInfoEntry(
        iso,
        levelIndex,
        new(0, 0),
        new(headerSector, source.Length / UyaLevelConstants.SectorSize),
        new(0, 0));
    var output = source.ToArray();
    output[3 * UyaLevelConstants.SectorSize] ^= 0xff;

    using var stream = new MemoryStream(iso, writable: false);
    var plan = IsoPatchPlanner.Create(GameId.UYA, stream, levelIndex, output);
    Expect(plan.SchemaVersion == IsoPatchPlanner.SchemaVersion
        && plan.FitsInPlace
        && plan.CapacitySectors == 9
        && plan.RequiredSectors == 9
        && plan.Ranges.Count == 2,
        "UYA ISO patch planning should record the supported in-place layout");
    Expect(plan.Ranges[0].Offset == headerSector * (long)UyaLevelConstants.SectorSize
        && plan.Ranges[1].Offset == (payloadBaseSector + 1L) * UyaLevelConstants.SectorSize
        && plan.Ranges.All(value => value.Alignment == UyaLevelConstants.SectorSize
            && value.SourceSha256.Length == 64
            && value.OutputSha256.Length == 64),
        "UYA ISO patch ranges should retain aligned preimage and result hashes");

    using var patchStream = new MemoryStream(iso.ToArray(), writable: true);
    IsoPatchApplier.ValidateSource(GameId.UYA, patchStream, plan);
    for (var index = 0; index < plan.Ranges.Count; index++)
        IsoPatchApplier.ApplyRange(GameId.UYA, patchStream, plan, index);
    IsoPatchApplier.VerifyOutput(GameId.UYA, patchStream, plan);
    var patched = patchStream.ToArray();
    using var patchedStream = new MemoryStream(patched, writable: false);
    Expect(UyaLooseLevelWadExtractor.ExtractPrimary(patchedStream, levelIndex).Bytes.SequenceEqual(output),
        "UYA ISO patch ranges should reconstruct the planned loose level WAD");

    Array.Resize(ref output, output.Length + UyaLevelConstants.SectorSize);
    WriteUyaFileBlock(output, 0x48, new UyaFileBlock(8, 2));
    stream.Position = 0;
    var fallback = IsoPatchPlanner.Create(GameId.UYA, stream, levelIndex, output);
    Expect(!fallback.FitsInPlace
        && fallback.Ranges.Count == 0
        && fallback.RequiredSectors == 10
        && fallback.Replacement is not null
        && fallback.Replacement.OutputIsoLength == iso.Length + output.Length,
        "UYA ISO patch planning should identify full-image fallback capacity");
    stream.Position = 0;
    using var replacementStream = new MemoryStream();
    IsoReplacementBuilder.BuildAsync(GameId.UYA, stream, replacementStream, fallback).GetAwaiter().GetResult();
    IsoReplacementBuilder.Verify(GameId.UYA, replacementStream, fallback);
    var replacement = fallback.Replacement!;
    Expect(replacementStream.Length == replacement.OutputIsoLength,
        "UYA full-image replacement should publish its final image size");
    replacementStream.Position = 0;
    Expect(UyaLooseLevelWadExtractor.ExtractPrimary(replacementStream, levelIndex).Bytes
            .SequenceEqual(replacement.LevelWadBytes.ToArray()),
        "UYA full-image replacement should install the relocated level payload");

    using var layoutStream = new MemoryStream(iso, writable: false);
    var retailLayout = UyaLevelInfoReader.ReadLevelSet(layoutStream, levelIndex);
    var compactOutput = source.ToArray();
    compactOutput[3 * UyaLevelConstants.SectorSize] ^= 0x7f;
    replacementStream.Position = 0;
    var retailPlan = IsoPatchPlanner.Create(
        GameId.UYA,
        replacementStream,
        new IsoLevelAllocation(retailLayout.RequestedLevelIndex, retailLayout.RequestedLevel.LevelWad.Offset,
            retailLayout.RequestedLevel.LevelWad.Length),
        compactOutput);
    Expect(retailPlan.FitsInPlace
        && retailPlan.HeaderSector == headerSector
        && retailPlan.Ranges.Any(value => value.Name == "level-info"),
        "UYA ISO patch planning should restore the retail layout when compact output fits");
    using var retailPatchStream = new MemoryStream(replacementStream.ToArray(), writable: true);
    for (var index = 0; index < retailPlan.Ranges.Count; index++)
        IsoPatchApplier.ApplyRange(GameId.UYA, retailPatchStream, retailPlan, index);
    IsoPatchApplier.VerifyInstalledLevel(GameId.UYA, retailPatchStream, retailPlan);
    Expect(UyaLevelInfoReader.ReadEntry(retailPatchStream, levelIndex).LevelWad == retailLayout.RequestedLevel.LevelWad,
        "UYA compact patch should restore the original level table entry for savestate compatibility");

    stream.Position = 0;
    var forced = IsoPatchPlanner.Create(GameId.UYA, stream, levelIndex, source, forceFullImage: true);
    Expect(!forced.FitsInPlace && forced.Replacement is not null
        && forced.StrategyReason.Contains("explicitly", StringComparison.Ordinal),
        "UYA ISO patch planning should explain a forced full-image replacement");

    replacementStream.Position = replacement.HeaderSector * (long)UyaLevelConstants.SectorSize;
    replacementStream.WriteByte(0xff);
    ExpectThrows<IOException>(() => IsoReplacementBuilder.Verify(GameId.UYA, replacementStream, fallback));

    var wrongLevel = source.ToArray();
    WriteInt32(wrongLevel, 0x08, levelIndex + 1);
    stream.Position = 0;
    ExpectThrows<InvalidDataException>(() => IsoPatchPlanner.Create(GameId.UYA, stream, levelIndex, wrongLevel));

    using var wrongPreimage = new MemoryStream(iso.ToArray(), writable: true);
    wrongPreimage.Position = plan.Ranges[0].Offset;
    wrongPreimage.WriteByte(0xff);
    ExpectThrows<InvalidDataException>(() => IsoPatchApplier.ValidateSource(GameId.UYA, wrongPreimage, plan));
    patchStream.Position = plan.Ranges[0].Offset;
    patchStream.WriteByte(0xff);
    ExpectThrows<IOException>(() => IsoPatchApplier.VerifyOutput(GameId.UYA, patchStream, plan));
}

static void ValidateTextureInventory()
{
    var palette = new byte[0x400];
    new byte[] { 1, 2, 3, 4 }.CopyTo(palette, 0 * 4);
    new byte[] { 8, 9, 10, 64 }.CopyTo(palette, 8 * 4);
    new byte[] { 16, 17, 18, 128 }.CopyTo(palette, 16 * 4);
    new byte[] { 7, 7, 7, 7 }.CopyTo(palette, 7 * 4);
    var pif = PifWriter.Write(PifWriter.CreateIndexed8(
        2, 2, palette, [8, 8, 16, 0], [[16]]));
    var inputs = new[]
    {
        new TextureInventoryInput(
            "tie-asset", TextureAssetFamily.Tie, 2, 0, TextureRole.Material, pif),
        new TextureInventoryInput(
            "moby-asset", TextureAssetFamily.Moby, 3, 1, TextureRole.Material, pif,
            [3, 1, 3], [7, 0]),
    };
    var inventory = TextureInventoryBuilder.Build(inputs.Reverse());
    Expect(inventory.SchemaVersion == TextureInventoryBuilder.SchemaVersion
        && inventory.Textures.Select(value => value.Family)
            .SequenceEqual([TextureAssetFamily.Moby, TextureAssetFamily.Tie])
        && inventory.TexelCount == 10,
        "UYA texture inventory should be deterministic and count every base/mip texel");
    var moby = inventory.Textures[0];
    var sourceEight = moby.IndexUsages.Single(value => value.SourcePixelIndex == 8);
    var sourceSixteen = moby.IndexUsages.Single(value => value.SourcePixelIndex == 16);
    Expect(sourceEight.PaletteIndex == 16
        && sourceEight.Color == new TextureColor(16, 17, 18, 128)
        && sourceEight.Frequency == 2
        && sourceEight.MipFrequencies.SequenceEqual([2, 0])
        && sourceSixteen.PaletteIndex == 8
        && sourceSixteen.Frequency == 2
        && sourceSixteen.MipFrequencies.SequenceEqual([1, 1]),
        "UYA texture inventory should retain raw indexes, CLUT-remapped indexes, exact colors, and mip frequencies");
    Expect(moby.MaterialSlots.SequenceEqual([1, 3])
        && moby.PaletteEntries[0] is { Referenced: true, Reserved: true }
        && moby.PaletteEntries[7] is { Referenced: false, Reserved: true }
        && moby.PaletteEntries[2] is { Referenced: false, Reserved: false },
        "UYA texture inventory should distinguish material use, referenced, reserved, and unused entries");
    var repeat = TextureInventoryBuilder.Build(inputs);
    Expect(JsonSerializer.SerializeToUtf8Bytes(inventory).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(repeat)),
        "equivalent UYA texture inputs should serialize identically regardless of input order");

    var indexed4 = new byte[PifHeader.SizeInBytes + 0x40 + 1];
    WriteInt32(indexed4, 0x00, PifHeader.ExpectedMagic);
    WriteInt32(indexed4, 0x04, indexed4.Length);
    WriteInt32(indexed4, 0x08, 1);
    WriteInt32(indexed4, 0x0c, 1);
    WriteInt32(indexed4, 0x10, (int)PifTextureEncoding.Indexed4);
    WriteInt32(indexed4, 0x1c, 1);
    indexed4[^1] = 0x0b;
    var indexed4Inventory = TextureInventoryBuilder.Build([
        new("shrub-asset", TextureAssetFamily.Shrub, 4, 0, TextureRole.Billboard, indexed4),
    ]).Textures.Single();
    Expect(indexed4Inventory.TexelCount == 1
        && indexed4Inventory.IndexUsages.Single() is { SourcePixelIndex: 11, PaletteIndex: 11, Frequency: 1 },
        "UYA texture inventory should retain the final odd indexed4 texel");

    var halfPalette = new byte[0x200];
    var invalidIndex = PifWriter.Write(PifWriter.CreateIndexed8(1, 1, halfPalette, [255]));
    ExpectThrows<InvalidDataException>(() => TextureInventoryBuilder.Build([
        new("bad-index", TextureAssetFamily.Tie, 1, 0, TextureRole.Material, invalidIndex),
    ]));
    ExpectThrows<InvalidDataException>(() => TextureInventoryBuilder.Build([
        new("bad-size", TextureAssetFamily.Tie, 1, 0, TextureRole.Material, pif.Concat(new byte[] { 0 }).ToArray()),
    ]));
    ExpectThrows<ArgumentException>(() => TextureInventoryBuilder.Build([inputs[0], inputs[0]]));
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    ExpectThrows<OperationCanceledException>(() => TextureInventoryBuilder.Build(inputs, cancellation.Token));
}

static void ValidatePaletteOptimization()
{
    var sizes = new[] { 10, 8, 5, 3, 3, 3 };
    var offset = 0;
    var textures = sizes.Select((size, index) =>
    {
        var colors = Enumerable.Range(offset, size).Select(SyntheticColor).ToArray();
        offset += size;
        return SyntheticInventoryTexture($"texture-{index}", colors, capacity: 16);
    }).ToArray();
    var inventory = new TextureInventory(1, textures, sizes.Sum());
    var optimized = PaletteOptimizer.Optimize(inventory);
    Expect(optimized.IsProvenOptimal
        && optimized.Method == PaletteOptimizer.ExactMethod
        && optimized.Palettes.Count == 2
        && optimized.Assignments.Count == textures.Length
        && optimized.Violations.Count == 0,
        "UYA palette optimizer should beat best-fit and prove the known two-palette optimum");
    foreach (var assignment in optimized.Assignments)
    {
        var palette = optimized.Palettes.Single(value => value.PaletteIndex == assignment.PaletteIndex);
        foreach (var remap in assignment.IndexRemaps)
            Expect(palette.Entries.Single(value => value.PaletteIndex == remap.TargetPaletteIndex).Color == remap.Color,
                "UYA palette optimization must preserve every imported color exactly");
    }
    var repeat = PaletteOptimizer.Optimize(new(1, textures.Reverse().ToArray(), sizes.Sum()));
    Expect(JsonSerializer.SerializeToUtf8Bytes(optimized).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(repeat)),
        "UYA palette optimization should be independent of inventory order");

    var red = new TextureColor(255, 0, 0, 128);
    var blue = new TextureColor(0, 0, 255, 128);
    var reserved = new TextureInventory(1, [
        SyntheticInventoryTexture("reserved-red", [red], 16, (7, red)),
        SyntheticInventoryTexture("reserved-blue", [blue], 16, (7, blue)),
    ], 2);
    var reservedResult = PaletteOptimizer.Optimize(reserved);
    Expect(reservedResult.Palettes.Count == 2
        && reservedResult.Palettes.All(value => value.Entries.Single() is { PaletteIndex: 7, Reserved: true })
        && reservedResult.Assignments.All(value => value.IndexRemaps.Single().TargetPaletteIndex == 7),
        "conflicting reserved indexes should prevent otherwise compatible palette sharing");

    var formatZero = SyntheticInventoryTexture("format-zero", [red], 16);
    var formatOne = SyntheticInventoryTexture("format-one", [red], 16) with
    {
        Constraint = formatZero.Constraint with { PaletteFormat = 1 },
    };
    var formatResult = PaletteOptimizer.Optimize(new(1, [formatZero, formatOne], 2));
    Expect(formatResult.Palettes.Count == 2,
        "textures with incompatible palette formats should not share a palette");

    var largeTextures = textures.Concat(Enumerable.Range(0, 7)
        .Select(index => SyntheticInventoryTexture($"large-{index:D2}", [SyntheticColor(0)], 16)))
        .ToArray();
    var large = new TextureInventory(1, largeTextures, largeTextures.Sum(value => value.TexelCount));
    var largeResult = PaletteOptimizer.Optimize(large);
    Expect(!largeResult.IsProvenOptimal
        && largeResult.Method == PaletteOptimizer.HeuristicMethod
        && largeResult.Palettes.Count == 3,
        "large groups above the exact-search limit should be labeled heuristic");

    var infeasible = new TextureInventory(1, [
        SyntheticInventoryTexture("too-many", [SyntheticColor(0), SyntheticColor(1), SyntheticColor(2)], 2),
    ], 3);
    var failed = PaletteOptimizer.Optimize(infeasible);
    Expect(failed.Palettes.Count == 0
        && failed.Assignments.Count == 0
        && failed.Violations.Single().Code == "palette-capacity-exceeded",
        "infeasible palette inputs should return a violation without partial output");

    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    ExpectThrows<OperationCanceledException>(() => PaletteOptimizer.Optimize(inventory, cancellation.Token));
}

static TextureInventoryEntry SyntheticInventoryTexture(
    string key,
    IReadOnlyList<TextureColor> colors,
    int capacity,
    params (int Index, TextureColor Color)[] reserved)
{
    var palette = colors.Concat(reserved.Select(value => value.Color)).Distinct().ToArray();
    var entries = palette.Select(color =>
    {
        var reservedIndex = Array.FindIndex(reserved, value => value.Color == color);
        return new TexturePaletteEntry(
            reservedIndex >= 0 ? reserved[reservedIndex].Index : -1,
            color,
            colors.Contains(color),
            reservedIndex >= 0);
    }).ToList();
    var occupied = entries.Where(value => value.Reserved).Select(value => value.PaletteIndex).ToHashSet();
    var next = 0;
    for (var index = 0; index < entries.Count; index++)
    {
        if (entries[index].Reserved) continue;
        while (occupied.Contains(next)) next++;
        entries[index] = entries[index] with { PaletteIndex = next };
        occupied.Add(next++);
    }
    var byColor = entries.ToDictionary(value => value.Color, value => value.PaletteIndex);
    return new(
        key,
        key,
        TextureAssetFamily.Tie,
        1,
        0,
        TextureRole.Material,
        colors.Count,
        1,
        1,
        colors.Count,
        new string('0', 64),
        new(PifTextureEncoding.Indexed4, entries.Count, 0, 0, capacity),
        [0],
        entries.OrderBy(value => value.PaletteIndex).ToArray(),
        colors.Select((color, index) => new TextureIndexUsage(index, byColor[color], color, 1, [1])).ToArray());
}

static TextureColor SyntheticColor(int value) => new(
    (byte)value,
    (byte)(value >> 8),
    (byte)(value >> 16),
    128);

static void ValidateUyaStaticAssetComposition()
{
    var palette = new byte[0x400];
    new byte[] { 10, 20, 30, 128 }.CopyTo(palette, 8 * 4);
    new byte[] { 40, 50, 60, 64 }.CopyTo(palette, 16 * 4);
    var pixels = Enumerable.Range(0, 16).Select(index => (byte)(index % 2 == 0 ? 8 : 16)).ToArray();
    var sourcePif = PifWriter.Write(PifWriter.CreateIndexed8(
        4, 4, palette, pixels, [[8, 16, 8, 16], [16]]));

    var sourceInventory = TextureInventoryBuilder.Build([
        new("writer", TextureAssetFamily.Tie, 1, 0, TextureRole.Material, sourcePif),
    ]);
    var sourceOptimization = PaletteOptimizer.Optimize(sourceInventory);
    var rewrittenPif = PaletteTextureWriter.RewritePif(
        sourcePif,
        sourceOptimization.Assignments.Single(),
        sourceOptimization.Palettes.Single());
    Expect(DecodedPifColors(sourcePif).SequenceEqual(DecodedPifColors(rewrittenPif)),
        "UYA optimized PIF writing should preserve every base and mip texel exactly");

    var inputs = new[]
    {
        new StaticAssetInput(
            "moby", TextureAssetFamily.Moby, 0x100, StaticDefinition(TextureAssetFamily.Moby, 0x11),
            new byte[] { 0x11, 0x12 }, [new(TextureRole.Material, sourcePif)]),
        new StaticAssetInput(
            "moby-shared", TextureAssetFamily.Moby, 0x101, StaticDefinition(TextureAssetFamily.Moby, 0x12),
            new byte[] { 0x13 }, [new(TextureRole.Material, sourcePif)]),
        new StaticAssetInput(
            "tie", TextureAssetFamily.Tie, 0x200, StaticDefinition(TextureAssetFamily.Tie, 0x22),
            new byte[] { 0x21, 0x22, 0x23 }, [new(TextureRole.Material, sourcePif)]),
        new StaticAssetInput(
            "shrub", TextureAssetFamily.Shrub, 0x300, StaticDefinition(TextureAssetFamily.Shrub, 0x33),
            new byte[] { 0x31 },
            [new(TextureRole.Material, sourcePif), new(TextureRole.Billboard, sourcePif)]),
    };
    var composed = StaticAssetComposer.Compose(GameId.UYA, new byte[0xc0], new byte[0x10], [], inputs);
    var header = DlAssetReader.ReadHeader(composed.HeaderBytes);
    Expect(header is { MobyModelCount: 2, TieModelCount: 1, ShrubModelCount: 1 }
        && header is { MobyTextureCount: 1, TieTextureCount: 1, ShrubTextureCount: 1 }
        && composed.Optimization.Palettes.Count == 1,
        "UYA static composition should install all selected classes and share their exact palette");
    var mobys = DlAssetReader.ReadModelDefinitions(composed.HeaderBytes, header.MobyModelOffset, 2);
    var moby = mobys[0];
    var tie = DlAssetReader.ReadModelDefinitions(composed.HeaderBytes, header.TieModelOffset, 1).Single();
    var shrub = DlAssetReader.ReadShrubDefinitions(composed.HeaderBytes, header.ShrubModelOffset, 1).Single();
    Expect(moby is { ModelId: 0x100, Unknown8: 0x11 }
        && tie is { ModelId: 0x200, Unknown8: 0x22 }
        && shrub is { ModelId: 0x300, Unknown8: 0x33 }
        && moby.TextureIds[0] == 0 && mobys[1].TextureIds[0] == 0
        && tie.TextureIds[0] == 0 && shrub.TextureIds[0] == 0,
        "UYA static composition should preserve definition metadata and remap family texture IDs");
    Expect(composed.AssetWadBytes.AsSpan(moby.ModelOffset, 2).SequenceEqual(inputs[0].ModelBytes.Span)
        && composed.AssetWadBytes.AsSpan(mobys[1].ModelOffset, 1).SequenceEqual(inputs[1].ModelBytes.Span)
        && composed.AssetWadBytes.AsSpan(tie.ModelOffset, 3).SequenceEqual(inputs[2].ModelBytes.Span)
        && composed.AssetWadBytes.AsSpan(shrub.ModelOffset, 1).SequenceEqual(inputs[3].ModelBytes.Span),
        "UYA static composition should install exact selected model bytes");

    var mobyTexture = DlAssetReader.ReadTextureDefinitions(
        composed.HeaderBytes, header.MobyTextureOffset, 1).Single();
    var tieTexture = DlAssetReader.ReadTextureDefinitions(
        composed.HeaderBytes, header.TieTextureOffset, 1).Single();
    var shrubTexture = DlAssetReader.ReadTextureDefinitions(
        composed.HeaderBytes, header.ShrubTextureOffset, 1).Single();
    Expect(mobyTexture.PaletteId == tieTexture.PaletteId
        && tieTexture.PaletteId == shrubTexture.PaletteId
        && shrubTexture.PaletteId == shrub.PaletteId,
        "moby, tie, shrub, and billboard definitions should reference the shared palette");
    foreach (var (name, definition) in new[]
        {
            ("moby", mobyTexture),
            ("tie", tieTexture),
            ("shrub", shrubTexture),
        })
    {
        var outputPif = DlAssetReader.BuildAssetTexture(
            name, 0, definition, composed.PaletteBytes, composed.AssetWadBytes,
            header.TextureDataOffset, isSwizzled: false).PifBytes;
        Expect(DecodedPifColors(sourcePif).SequenceEqual(DecodedPifColors(outputPif)),
            $"composed {name} texture should preserve every source texel");
    }
    var billboardPif = DlAssetReader.BuildShrubBillboardTexture(shrub, composed.PaletteBytes).PifBytes;
    Expect(DecodedPifColors(sourcePif).SequenceEqual(DecodedPifColors(billboardPif)),
        "composed shrub billboard should preserve every source texel");
}

static byte[] StaticDefinition(TextureAssetFamily family, int marker)
{
    var bytes = new byte[family == TextureAssetFamily.Shrub ? 0x30 : 0x20];
    WriteInt32(bytes, 0x08, marker);
    bytes.AsSpan(0x10, 0x10).Fill(byte.MaxValue);
    return bytes;
}

static IReadOnlyList<TextureColor> DecodedPifColors(byte[] bytes)
{
    var texture = PifReader.Read(bytes);
    var colors = new List<TextureColor>();
    var width = texture.Header.USize;
    var height = texture.Header.VSize;
    foreach (var (pixels, level) in new[] { texture.PixelData }.Concat(texture.MipPixelData).Select((value, index) => (value, index)))
    {
        if (level > 0)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        for (var texel = 0; texel < width * height; texel++)
        {
            var sourceIndex = texture.Encoding == PifTextureEncoding.Indexed8
                ? pixels[texel]
                : pixels[texel / 2] >> (texel % 2 * 4) & 0x0f;
            var paletteIndex = texture.Encoding == PifTextureEncoding.Indexed8
                ? TextureConverter.DecodePaletteIndex((byte)sourceIndex)
                : sourceIndex;
            var offset = paletteIndex * 4;
            colors.Add(new(
                texture.PaletteData[offset],
                texture.PaletteData[offset + 1],
                texture.PaletteData[offset + 2],
                texture.PaletteData[offset + 3]));
        }
    }
    return colors;
}

static void ValidateUyaArchiveQualificationCorpus()
{
    var smallest = new byte[UyaLevelConstants.SectorSize];
    WriteInt32(smallest, 0, UyaLevelConstants.LevelWadHeaderSize);

    var sparse = new byte[UyaLevelConstants.SectorSize * 32];
    WriteInt32(sparse, 0, UyaLevelConstants.LevelWadHeaderSize);

    var alignmentHeavy = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);

    var largestShape = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    Array.Resize(ref largestShape, UyaLevelConstants.SectorSize * 128);
    WriteUyaFileBlock(largestShape, 0x48, new UyaFileBlock(8, 120));

    foreach (var (name, bytes) in new[]
        {
            ("smallest", smallest),
            ("sparse", sparse),
            ("alignment-heavy", alignmentHeavy),
            ("largest-shape", largestShape),
        })
    {
        var result = UyaArchiveQualification.Measure(0, bytes);
        Expect(result.Succeeded && result.HashChecks.All(check => check.Matched)
            && result.BulkBufferCopyCount == result.ContainerCount + 1,
            $"UYA archive qualification {name} layout should pass every hash check");
    }

    var malformed = UyaArchiveQualification.Measure(0, new byte[8]);
    Expect(!malformed.Succeeded && malformed.Diagnostics.Count > 0,
        "UYA archive qualification malformed layout should retain diagnostics without output");
}

static void ValidateUyaLooseLevelWadExtraction()
{
    const int levelIndex = 3;
    const int headerSector = 20;
    const int payloadBaseSector = 60;
    var looseWadBytes = CreateSyntheticUyaLooseLevelWad(payloadBaseSector);
    var iso = CreateSyntheticUyaIso(levelIndex, headerSector, payloadBaseSector, looseWadBytes);

    using var stream = new MemoryStream(iso, writable: false);
    var extracted = UyaLooseLevelWadExtractor.ExtractPrimary(stream, levelIndex);

    Expect(extracted.LevelIndex == levelIndex, "UYA loose WAD extraction should preserve requested level index");
    Expect(extracted.HeaderSector == headerSector, "UYA loose WAD extraction should report the header sector");
    Expect(extracted.PayloadBaseSector == payloadBaseSector, "UYA loose WAD extraction should report the payload base sector");
    Expect(extracted.SectorCount == looseWadBytes.Length / UyaLevelConstants.SectorSize, "UYA loose WAD extraction should copy through the last referenced sector");
    Expect(extracted.Bytes.SequenceEqual(looseWadBytes), "UYA loose WAD extraction should preserve referenced WAD bytes in a self-contained layout");
}

static void ValidateUyaDetachedWadExtraction()
{
    const int headerSector = 2;
    const int payloadBaseSector = 8;
    var iso = new byte[11 * UyaLevelConstants.SectorSize];
    var headerOffset = headerSector * UyaLevelConstants.SectorSize;
    WriteInt32(iso, headerOffset, UyaLevelConstants.SectorSize * 2);
    WriteInt32(iso, headerOffset + sizeof(int), payloadBaseSector);
    iso[headerOffset + UyaLevelConstants.SectorSize] = 0x42;
    iso[(payloadBaseSector + 2) * UyaLevelConstants.SectorSize] = 0x73;

    using var stream = new MemoryStream(iso, writable: false);
    var wad = UyaLooseLevelWadExtractor.ExtractDetached(stream, new UyaFileBlock(headerSector, 3));

    Expect(wad.Length == 3 * UyaLevelConstants.SectorSize, "detached WAD extraction should preserve the table size");
    Expect(wad[UyaLevelConstants.SectorSize] == 0x42, "detached WAD extraction should copy the full detached header");
    Expect(wad[2 * UyaLevelConstants.SectorSize] == 0x73, "detached WAD extraction should preserve payload sectors");
}

static void ValidateUyaLooseLevelWadUnpacking()
{
    var looseWadBytes = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 20);
    var package = UyaLevelWadUnpacker.Unpack(looseWadBytes);
    var files = package.Files.ToDictionary(file => file.Path);

    Expect(files.ContainsKey("level_wad/header.bin"), "UYA loose WAD unpack should include the level WAD header");
    Expect(files["level_wad/level_data.wad"].Bytes[0x80] == 0x11, "UYA loose WAD unpack should include level data bytes");
    Expect(files["level_wad/sound.bnk"].Bytes[0] == 0x41, "UYA loose WAD unpack should include sound bank bytes");
    Expect(files["gameplay/gameplay.bin"].Bytes[0] == UyaGameplayBlockReader.CoreHeaderSize, "UYA loose WAD unpack should include gameplay bytes");
    Expect(files["gameplay/gameplay_core.bin"].Bytes.Length >= UyaGameplayBlockReader.CoreHeaderSize, "UYA loose WAD unpack should decompress gameplay bytes");
    Expect(files["gameplay/core/header.bin"].Bytes.Length == UyaGameplayBlockReader.CoreHeaderSize, "UYA loose WAD unpack should expose the gameplay pointer table");
    Expect(files["gameplay/core/level_settings.bin"].Bytes.SequenceEqual(new byte[] { 0xA1, 0xA2 }), "UYA loose WAD unpack should split gameplay level settings");
    Expect(files["gameplay/core/directional_lights.bin"].Bytes.SequenceEqual(new byte[] { 0xB1, 0xB2 }), "UYA loose WAD unpack should split gameplay directional lights");
    Expect(files["gameplay/core/us_english_strings.bin"].Bytes.SequenceEqual(new byte[] { 0xD1, 0xD2 }), "UYA loose WAD unpack should name language blocks as strings");
    Expect(files["gameplay/core/splines.bin"].Bytes.SequenceEqual(new byte[] { 0xE1, 0xE2 }), "UYA loose WAD unpack should name path blocks as splines");
    Expect(files["gameplay/core/grind_splines.bin"].Bytes[0x10..0x12].SequenceEqual(new byte[] { 0xF1, 0xF2 }), "UYA loose WAD unpack should name grind path blocks as grind splines");
    Expect(files["gameplay/core/moby_instances.bin"].Bytes[..2].SequenceEqual(new byte[] { 0xC1, 0xC2 }), "UYA loose WAD unpack should split gameplay moby instances");
    Expect(files["occlusion/occlusion.bin"].Bytes[0] == 0x61, "UYA loose WAD unpack should include occlusion bytes");
    Expect(files["level_wad/chunks/chunk0.wad"].Bytes[0] == 0x71, "UYA loose WAD unpack should include chunk bytes");
    Expect(files["level_wad/chunks/chunk0_bank.wad"].Bytes[0] == 0x81, "UYA loose WAD unpack should include chunk bank bytes");
    Expect(files["code/code.bin"].Bytes.SequenceEqual(new byte[] { 0x11, 0x12, 0x13, 0x14 }), "UYA loose WAD unpack should expose code payload");
    Expect(files["assets/asset_header.bin"].Bytes.SequenceEqual(new byte[] { 0x21, 0x22, 0x23, 0x24 }), "UYA loose WAD unpack should expose asset header payload");
    Expect(files["assets/palette.bin"].Bytes.SequenceEqual(new byte[] { 0x31, 0x32, 0x33, 0x34 }), "UYA loose WAD unpack should expose palette payload");
    Expect(files["hud/header.bin"].Bytes.SequenceEqual(new byte[] { 0x41, 0x42, 0x43, 0x44 }), "UYA loose WAD unpack should expose HUD header payload");
    Expect(files["hud/bank0.bin"].Bytes.SequenceEqual(new byte[] { 0x51, 0x52, 0x53, 0x54 }), "UYA loose WAD unpack should expose HUD bank payload");
    Expect(files["assets/asset_wad.bin"].Bytes.SequenceEqual(new byte[] { 0x61, 0x62, 0x63, 0x64 }), "UYA loose WAD unpack should expose asset WAD payload");
    Expect(files["transition_textures/transition_textures.bin"].Bytes.SequenceEqual(new byte[] { 0x71, 0x72, 0x73, 0x74 }), "UYA loose WAD unpack should expose transition texture payload");

    var packed = package.ToPackedPackage();
    Expect(packed.Entries.Count == package.Files.Count, "UYA packed package entry count should match loose file count");
    var packedCodeEntry = packed.Entries.Single(entry => entry.Path == "code/code.bin");
    var packedCodeBytes = packed.PackedBytes.AsSpan(packedCodeEntry.Offset, packedCodeEntry.Length).ToArray();
    Expect(packedCodeBytes.SequenceEqual(files["code/code.bin"].Bytes), "UYA packed package offsets should round-trip entry bytes");
}

static void ValidateUyaStandaloneGameplayUnpacking()
{
    var files = UyaLevelWadUnpacker
        .UnpackGameplay(CreateSyntheticUyaGameplay())
        .ToDictionary(file => file.Path);

    Expect(files["gameplay/gameplay.bin"].Bytes[0] == UyaGameplayBlockReader.CoreHeaderSize, "UYA standalone gameplay unpack should include raw gameplay bytes");
    Expect(files["gameplay/gameplay_core.bin"].Bytes.Length >= UyaGameplayBlockReader.CoreHeaderSize, "UYA standalone gameplay unpack should expose core gameplay bytes");
    Expect(files["gameplay/core/level_settings.bin"].Bytes.SequenceEqual(new byte[] { 0xA1, 0xA2 }), "UYA standalone gameplay unpack should split level settings");
    Expect(files["gameplay/core/splines.bin"].Bytes.SequenceEqual(new byte[] { 0xE1, 0xE2 }), "UYA standalone gameplay unpack should split splines");
}

static void ValidateUyaStandaloneLevelDataUnpacking()
{
    var files = UyaLevelWadUnpacker
        .UnpackLevelData(CreateSyntheticUyaLevelData())
        .ToDictionary(file => file.Path);

    Expect(files["level_wad/level_data.wad"].Bytes[0x80] == 0x11, "UYA standalone level data unpack should include raw level data bytes");
    Expect(files["code/code.bin"].Bytes.SequenceEqual(new byte[] { 0x11, 0x12, 0x13, 0x14 }), "UYA standalone level data unpack should expose code payload");
    Expect(files["assets/asset_header.bin"].Bytes.SequenceEqual(new byte[] { 0x21, 0x22, 0x23, 0x24 }), "UYA standalone level data unpack should expose asset header payload");
    Expect(files["assets/asset_wad.bin"].Bytes.SequenceEqual(new byte[] { 0x61, 0x62, 0x63, 0x64 }), "UYA standalone level data unpack should expose asset WAD payload");
}

static void ValidateUyaCustomMapZipUnpacking()
{
    using var zipStream = new MemoryStream();
    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        AddZipEntry(archive, "maps/example.wad", CreateSyntheticUyaLevelData());
        AddZipEntry(archive, "maps/example.world", CreateSyntheticUyaGameplay());
    }

    var package = UyaCustomMapZipUnpacker.Unpack(zipStream.ToArray());

    Expect(package.LevelDataWadEntryName == "maps/example.wad", "UYA custom map zip unpack should report the level data entry name");
    Expect(package.WorldEntryName == "maps/example.world", "UYA custom map zip unpack should report the world entry name");
    Expect(package.LevelDataFiles.Any(file => file.Path == "assets/asset_header.bin"), "UYA custom map zip unpack should expose level-data files");
    Expect(package.GameplayFiles.Any(file => file.Path == "gameplay/core/splines.bin"), "UYA custom map zip unpack should expose gameplay files");
    Expect(package.Files.Count == package.LevelDataFiles.Count + package.GameplayFiles.Count, "UYA custom map zip package should combine level-data and gameplay files");
}

static void ValidateUyaGameplayTypedParsing()
{
    var levelSettingsBytes = new byte[UyaLevelSettingsReader.MinimumSize + 2];
    WriteInt32(levelSettingsBytes, 0x00, 57);
    WriteInt32(levelSettingsBytes, 0x04, 65);
    WriteInt32(levelSettingsBytes, 0x08, 50);
    WriteInt32(levelSettingsBytes, 0x0c, 40);
    WriteInt32(levelSettingsBytes, 0x10, 50);
    WriteInt32(levelSettingsBytes, 0x14, 40);
    WriteSingle(levelSettingsBytes, 0x18, 61440);
    WriteSingle(levelSettingsBytes, 0x1c, 179200);
    WriteSingle(levelSettingsBytes, 0x20, 255);
    WriteSingle(levelSettingsBytes, 0x24, 63.75f);
    WriteSingle(levelSettingsBytes, 0x28, -100);
    WriteInt32(levelSettingsBytes, 0x2c, 1);
    WriteSingle(levelSettingsBytes, 0x30, 1);
    WriteSingle(levelSettingsBytes, 0x34, 2);
    WriteSingle(levelSettingsBytes, 0x38, 3);
    WriteSingle(levelSettingsBytes, 0x3c, 20);
    WriteSingle(levelSettingsBytes, 0x40, 21);
    WriteSingle(levelSettingsBytes, 0x44, 22);
    WriteSingle(levelSettingsBytes, 0x48, 0.5f);
    WriteInt32(levelSettingsBytes, 0x4c, -1);
    WriteInt32(levelSettingsBytes, 0x50, 2);
    WriteInt32(levelSettingsBytes, 0x54, 3);
    WriteUInt32(levelSettingsBytes, 0x58, 0x12345678);
    WriteInt32(levelSettingsBytes, 0x7c, 59);
    WriteInt32(levelSettingsBytes, 0x80, 1234);
    levelSettingsBytes[^2] = 0xaa;
    levelSettingsBytes[^1] = 0xbb;

    var cameraCollisionBytes = new byte[0x4050];
    WriteInt32(cameraCollisionBytes, 0x10, 0x4000);
    WriteInt32(cameraCollisionBytes, 0x4010, 1);
    WriteSingle(cameraCollisionBytes, 0x4020, 10);
    WriteSingle(cameraCollisionBytes, 0x4024, 20);
    WriteSingle(cameraCollisionBytes, 0x4028, 0);
    WriteSingle(cameraCollisionBytes, 0x402c, 8);
    WriteInt32(cameraCollisionBytes, 0x4030, 3);
    WriteInt32(cameraCollisionBytes, 0x4034, 2);
    WriteInt32(cameraCollisionBytes, 0x4038, 0x40);
    WriteInt32(cameraCollisionBytes, 0x403c, 7);
    WriteSingle(cameraCollisionBytes, 0x4040, 1.5f);

    var mobyBytes = new byte[UyaMobyInstancesReader.HeaderSize + UyaMobyInstancesReader.RecordSize];
    WriteInt32(mobyBytes, 0x00, 1);
    WriteInt32(mobyBytes, 0x04, 400);
    WriteInt32(mobyBytes, 0x08, 8);
    WriteInt32(mobyBytes, 0x0c, 9);

    const int mobyOffset = UyaMobyInstancesReader.HeaderSize;
    WriteInt32(mobyBytes, mobyOffset, UyaMobyInstancesReader.RecordSize);
    WriteInt32(mobyBytes, mobyOffset + 0x04, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x08, 8);
    WriteInt32(mobyBytes, mobyOffset + 0x0c, 12);
    WriteInt32(mobyBytes, mobyOffset + 0x10, 0x78);
    WriteInt32(mobyBytes, mobyOffset + 0x14, 4);
    WriteInt32(mobyBytes, mobyOffset + 0x18, 18);
    WriteInt32(mobyBytes, mobyOffset + 0x1c, 28);
    WriteInt32(mobyBytes, mobyOffset + 0x20, 32);
    WriteInt32(mobyBytes, mobyOffset + 0x24, 36);
    WriteInt32(mobyBytes, mobyOffset + 0x28, 0x107c);
    WriteSingle(mobyBytes, mobyOffset + 0x2c, 1.5f);
    WriteInt32(mobyBytes, mobyOffset + 0x30, 64);
    WriteInt32(mobyBytes, mobyOffset + 0x34, 80);
    WriteInt32(mobyBytes, mobyOffset + 0x38, 32);
    WriteInt32(mobyBytes, mobyOffset + 0x3c, 64);
    WriteSingle(mobyBytes, mobyOffset + 0x40, 10);
    WriteSingle(mobyBytes, mobyOffset + 0x44, 20);
    WriteSingle(mobyBytes, mobyOffset + 0x48, 30);
    WriteSingle(mobyBytes, mobyOffset + 0x4c, 0.25f);
    WriteSingle(mobyBytes, mobyOffset + 0x50, 0.5f);
    WriteSingle(mobyBytes, mobyOffset + 0x54, 0.75f);
    WriteInt32(mobyBytes, mobyOffset + 0x58, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x5c, 1);
    WriteSingle(mobyBytes, mobyOffset + 0x60, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x64, 1);
    WriteInt32(mobyBytes, mobyOffset + 0x68, 10);
    WriteInt32(mobyBytes, mobyOffset + 0x6c, 1);
    WriteInt32(mobyBytes, mobyOffset + 0x70, 0x54);
    WriteInt32(mobyBytes, mobyOffset + 0x74, 86);
    WriteInt32(mobyBytes, mobyOffset + 0x78, 77);
    WriteInt32(mobyBytes, mobyOffset + 0x7c, 2);
    WriteInt32(mobyBytes, mobyOffset + 0x80, 2);
    WriteInt32(mobyBytes, mobyOffset + 0x84, -1);

    var gameplay = UyaGameplayBlockReader.ReadCore(BuildGameplayData(
        UyaGameplayBlockReader.CoreHeaderSize,
        (0x00, levelSettingsBytes),
        (0x4c, mobyBytes),
        (0x58, [0x01, 0x02]),
        (0x5c, [0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00]),
        (0x60, [0xde, 0xad, 0xbe, 0xef]),
        (0x64, [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff]),
        (0x88, cameraCollisionBytes)));
    var settings = gameplay.Blocks.Single(block => block.SemanticName == "level_settings").LevelSettings;
    var mobyInstances = gameplay.Blocks.Single(block => block.SemanticName == "moby_instances").MobyInstances;

    Expect(settings is not null, "UYA core level_settings block should be parsed into a typed model");
    Expect(settings!.BackgroundColor == new UyaRgb96(57, 65, 50), "UYA level settings background color should be parsed");
    Expect(settings.FogColor == new UyaRgb96(40, 50, 40), "UYA level settings fog color should be parsed");
    Expect(settings.FogFarDistance == 179200, "UYA level settings fog far distance should be parsed");
    Expect(settings.IsSphericalWorld, "UYA level settings spherical world flag should be parsed");
    Expect(settings.SphereCenter == new UyaVector3(1, 2, 3), "UYA level settings sphere center should be parsed");
    Expect(settings.ShipPosition == new UyaVector3(20, 21, 22), "UYA level settings ship position should use DL axis order");
    Expect(settings.ShipPath == -1, "UYA level settings ship path should be parsed");
    Expect(settings.ChunkPlanes.Count == 0, "UYA empty level settings chunk plane terminator should be skipped");
    Expect(settings.CoreSoundsCount == 59, "UYA level settings core sound count should be parsed");
    Expect(settings.Rac3ThirdPart == 1234, "UYA level settings R&C3 tail field should be parsed");
    Expect(settings.TrailingBytes.SequenceEqual(new byte[] { 0xaa, 0xbb }), "UYA level settings trailing bytes should be preserved");

    var editedSettingsBytes = UyaLevelSettingsWriter.Write(levelSettingsBytes, new(
        new(1, 2, 3), new(4, 5, 6), 1024, 2048, 127.5f, 32));
    var editedSettings = UyaLevelSettingsReader.Read(editedSettingsBytes);
    Expect(editedSettings.BackgroundColor == new UyaRgb96(1, 2, 3), "UYA level settings background color should serialize");
    Expect(editedSettings.FogColor == new UyaRgb96(4, 5, 6), "UYA level settings fog color should serialize");
    Expect(editedSettings.FogNearDistance == 1024 && editedSettings.FogFarDistance == 2048,
        "UYA level settings fog distances should serialize");
    Expect(editedSettings.FogNearIntensity == 127.5f && editedSettings.FogFarIntensity == 32,
        "UYA level settings fog intensities should serialize");
    Expect(editedSettingsBytes.AsSpan(0x28).SequenceEqual(levelSettingsBytes.AsSpan(0x28)),
        "UYA level settings writer should preserve unsupported bytes");

    Expect(mobyInstances is not null, "UYA core moby_instances block should be parsed into a typed model");
    Expect(mobyInstances!.StaticCount == 1, "UYA moby instance static count should be parsed");
    Expect(mobyInstances.SpawnableMobyCount == 400, "UYA moby instance spawnable count should be parsed");
    Expect(mobyInstances.Pad8 == 8 && mobyInstances.PadC == 9, "UYA moby instance header padding should be parsed");

    var moby = mobyInstances.Instances.Single();
    Expect(moby.Size == UyaMobyInstancesReader.RecordSize, "UYA moby instance size field should be parsed");
    Expect(moby.Mission == -1, "UYA moby instance mission should be parsed");
    Expect(moby.Uid == 0x78, "UYA moby instance uid should be parsed");
    Expect(moby.Bolts == 4, "UYA moby instance bolts should be parsed");
    Expect(moby.ClassId == 0x107c, "UYA moby instance class id should be parsed");
    Expect(moby.Scale == 1.5f, "UYA moby instance scale should be parsed");
    Expect(moby.Position == new UyaVector3(10, 20, 30), "UYA moby instance position should use DL axis order");
    Expect(moby.Rotation == new UyaVector3(0.25f, 0.5f, 0.75f), "UYA moby instance rotation should use DL axis order");
    Expect(moby.PvarIndex == 10, "UYA moby instance pvar index should be parsed");
    Expect(moby.Color == new UyaRgb96(86, 77, 2), "UYA moby instance color should be parsed");
    Expect(moby.Unknown84 == -1, "UYA moby instance 0x84 field should be parsed");
    Expect(gameplay.Blocks.Single(block => block.SemanticName == "pvar_data").PayloadBytes.SequenceEqual(new byte[] { 0xde, 0xad, 0xbe, 0xef }), "UYA pvar data payload should be exposed");
    var cameraCollision = gameplay.Blocks.Single(block => block.SemanticName == "camera_collision_grid").CameraCollisionGrid;
    Expect(cameraCollision?.OccupiedCellCount == 1, "UYA camera collision occupied cell count should be parsed");
    Expect(cameraCollision?.Primitives.Single() is { Type: 3, Index: 2, Flags: 0x40, IntValue: 7, FloatValue: 1.5f },
        "UYA camera collision primitive metadata should be parsed");

    var occlusionBytes = new byte[0xb0];
    WriteInt32(occlusionBytes, 0, 0x30);
    WriteUInt16(occlusionBytes, 4, 1);
    WriteUInt16(occlusionBytes, 6, 1);
    WriteUInt16(occlusionBytes, 8, 3);
    WriteUInt16(occlusionBytes, 12, 2);
    WriteUInt16(occlusionBytes, 14, 1);
    WriteUInt16(occlusionBytes, 16, 5);
    WriteUInt16(occlusionBytes, 20, 3);
    WriteUInt16(occlusionBytes, 22, 1);
    WriteUInt16(occlusionBytes, 24, 0);
    var occlusion = UyaOcclusionGridReader.Read(occlusionBytes);
    Expect(occlusion.Octants.Single() == new UyaOcclusionOctant(3, 2, 1, 0),
        "UYA occlusion octant coordinates should be parsed");
    var assetHeader = new byte[0xc4];
    WriteInt32(assetHeader, 0x0c, 0x100);
    WriteInt32(assetHeader, 0x14, 0x1b0);
    var assetWad = new byte[0x1b0];
    occlusionBytes.CopyTo(assetWad, 0x100);
    var routedOcclusion = UyaOcclusionGridReader.ReadLevelAsset(assetHeader, assetWad);
    Expect(routedOcclusion.Octants.Single() == new UyaOcclusionOctant(3, 2, 1, 0),
        "UYA occlusion grid should be located through the level asset header");

    var editedMoby = new UyaMobyInstanceEdit(
        0x1234,
        new(100, 200, 300),
        new(0, 0, MathF.Sin(MathF.PI / 4), MathF.Cos(MathF.PI / 4)),
        2,
        mobyBytes.AsSpan(UyaMobyInstancesReader.HeaderSize, UyaMobyInstancesReader.RecordSize).ToArray());
    var rebuiltMobys = UyaMobyInstancesReader.Read(UyaMobyInstancesWriter.Write(mobyInstances, [editedMoby]));
    var rebuiltMoby = rebuiltMobys.Instances.Single();
    Expect(rebuiltMoby.ClassId == editedMoby.ClassId
        && rebuiltMoby.Position == editedMoby.Position
        && rebuiltMoby.Scale == editedMoby.Scale,
        "UYA moby writer should update class, position, and scale");
    Expect(MathF.Abs(rebuiltMoby.Rotation.Z - MathF.PI / 2) < 0.0001f,
        "UYA moby writer should encode quaternion rotation as native ZYX Euler angles");
    Expect(rebuiltMoby.PvarIndex == moby.PvarIndex && rebuiltMoby.Uid == moby.Uid,
        "UYA moby writer should preserve pvar and unsupported record fields");

    var gcLevelSettingsBytes = new byte[0x80];
    WriteInt32(gcLevelSettingsBytes, 0x00, 57);
    WriteInt32(gcLevelSettingsBytes, 0x04, 65);
    WriteInt32(gcLevelSettingsBytes, 0x08, 50);
    WriteSingle(gcLevelSettingsBytes, 0x18, 61440);
    WriteSingle(gcLevelSettingsBytes, 0x1c, 179200);
    var gcSettings = GcLevelSettingsReader.Read(gcLevelSettingsBytes);
    Expect(gcSettings.BackgroundColor == new GcRgb96(57, 65, 50), "GC level settings background color should be parsed");
    Expect(gcSettings.FogFarDistance == 179200, "GC level settings fog distance should be parsed");
}

static void ValidateUyaStaticInstanceParsing()
{
    var cameras = new byte[UyaCameraInstancesReader.HeaderSize + UyaCameraInstancesReader.RecordSize];
    WriteInt32(cameras, 0, 1);
    WriteInt32(cameras, 0x10, 7);
    WriteSingle(cameras, 0x14, 10);
    WriteSingle(cameras, 0x18, 20);
    WriteSingle(cameras, 0x1c, 30);
    WriteSingle(cameras, 0x20, 0.1f);
    WriteSingle(cameras, 0x24, 0.2f);
    WriteSingle(cameras, 0x28, 0.3f);
    WriteInt32(cameras, 0x2c, 4);

    var sounds = new byte[UyaSoundInstancesReader.HeaderSize + UyaSoundInstancesReader.RecordSize];
    WriteInt32(sounds, 0, 1);
    WriteInt16(sounds, 0x10, 8);
    WriteInt16(sounds, 0x12, 9);
    WriteInt32(sounds, 0x14, 0x12345678);
    WriteInt32(sounds, 0x18, 5);
    WriteSingle(sounds, 0x1c, 64);
    WriteSingle(sounds, 0x20, 2);
    WriteSingle(sounds, 0x34, 3);
    WriteSingle(sounds, 0x48, 4);
    WriteSingle(sounds, 0x50, 100);
    WriteSingle(sounds, 0x54, 200);
    WriteSingle(sounds, 0x58, 300);
    WriteSingle(sounds, 0x60, 0.5f);
    WriteSingle(sounds, 0x90, 0.1f);
    WriteSingle(sounds, 0x94, 0.2f);
    WriteSingle(sounds, 0x98, 0.3f);

    var ties = new byte[UyaTieInstancesReader.HeaderSize + UyaTieInstancesReader.RecordSize + 2];
    WriteInt32(ties, 0, 1);
    WriteInt32(ties, 4, 2);
    var tie = UyaTieInstancesReader.HeaderSize;
    WriteInt32(ties, tie, 0x2132);
    WriteSingle(ties, tie + 0x10, 2);
    WriteSingle(ties, tie + 0x24, 3);
    WriteSingle(ties, tie + 0x38, 4);
    WriteSingle(ties, tie + 0x40, 10);
    WriteSingle(ties, tie + 0x44, 20);
    WriteSingle(ties, tie + 0x48, 30);
    ties[^2] = 0xaa;
    ties[^1] = 0xbb;

    var shrubs = new byte[UyaShrubInstancesReader.HeaderSize + UyaShrubInstancesReader.RecordSize];
    WriteInt32(shrubs, 0, 1);
    var shrub = UyaShrubInstancesReader.HeaderSize;
    WriteInt32(shrubs, shrub, 0x20f0);
    WriteSingle(shrubs, shrub + 4, 256);
    WriteSingle(shrubs, shrub + 0x10, 1);
    WriteSingle(shrubs, shrub + 0x24, 1);
    WriteSingle(shrubs, shrub + 0x38, 1);
    WriteSingle(shrubs, shrub + 0x40, -5);

    var gameplay = UyaGameplayBlockReader.ReadCore(BuildGameplayData(
        UyaGameplayBlockReader.CoreHeaderSize,
        (0x08, cameras),
        (0x0c, sounds),
        (0x34, ties),
        (0x40, shrubs)));
    var parsedCameras = gameplay.Blocks.Single(block => block.SemanticName == "cameras").CameraInstances!;
    var parsedSounds = gameplay.Blocks.Single(block => block.SemanticName == "sound_instances").SoundInstances!;
    var parsedTies = gameplay.Blocks.Single(block => block.SemanticName == "tie_instances").TieInstances!;
    var parsedShrubs = gameplay.Blocks.Single(block => block.SemanticName == "shrub_instances").ShrubInstances!;

    Expect(parsedTies.Count == 1 && parsedTies.HeaderWords.SequenceEqual([2, 0, 0]), "UYA tie instance header should be parsed");
    Expect(parsedTies.Instances[0].ClassId == 0x2132, "UYA tie class should be parsed");
    Expect(parsedTies.Instances[0].Transform.BasisX.X == 2, "UYA tie transform should be parsed");
    Expect(parsedTies.Instances[0].Transform.Position == new UyaVector4(10, 20, 30, 0), "UYA tie position should be parsed");
    Expect(parsedTies.Instances[0].RawBytes.Length == UyaTieInstancesReader.RecordSize, "UYA tie raw record should be retained");
    Expect(parsedTies.TrailingBytes.SequenceEqual(new byte[] { 0xaa, 0xbb }), "UYA tie trailing bytes should be retained");
    Expect(parsedShrubs.Count == 1 && parsedShrubs.Instances[0].ClassId == 0x20f0, "UYA shrub class should be parsed");
    Expect(parsedShrubs.Instances[0].DrawDistance == 256, "UYA shrub draw distance should be parsed");
    Expect(parsedShrubs.Instances[0].Transform.Position.X == -5, "UYA shrub position should be parsed");
    Expect(parsedCameras.Instances.Single() is { Type: 7, PvarIndex: 4 }
        && parsedCameras.Instances.Single().Position == new GameplayVector3(10, 20, 30),
        "UYA camera instance should be parsed");
    Expect(parsedSounds.Instances.Single() is { ClassId: 8, MissionClass: 9, PvarIndex: 5, Range: 64 }
        && parsedSounds.Instances.Single().Matrix[15] == 0
        && parsedSounds.Instances.Single().Rotation.Z == 0.3f,
        "UYA sound instance should be parsed");
    ExpectThrows<InvalidDataException>(() => UyaTieInstancesReader.Read(ties.AsSpan(0, ties.Length - 3)));
    ExpectThrows<InvalidDataException>(() => UyaSoundInstancesReader.Read(sounds.AsSpan(0, sounds.Length - 1)));

    var edited = new UyaStaticInstanceEdit(
        0x3456,
        new(100, 200, 300),
        new(0, 0, MathF.Sin(MathF.PI / 4), MathF.Cos(MathF.PI / 4)),
        new(2, 3, 4),
        parsedTies.Instances[0].RawBytes);
    var rebuiltTies = UyaTieInstancesReader.Read(UyaTieInstancesWriter.Write(parsedTies, [edited]));
    var rebuiltTie = rebuiltTies.Instances.Single();
    Expect(rebuiltTies.HeaderWords.SequenceEqual(parsedTies.HeaderWords)
        && rebuiltTies.TrailingBytes.SequenceEqual(parsedTies.TrailingBytes),
        "UYA tie writer should preserve header and trailing bytes");
    Expect(rebuiltTie.ClassId == edited.ClassId
        && rebuiltTie.Transform.Position == new UyaVector4(100, 200, 300, parsedTies.Instances[0].Transform.Position.W),
        "UYA tie writer should update class and position while preserving the fourth component");
    Expect(MathF.Abs(rebuiltTie.Transform.BasisX.Y - 2) < 0.0001f
        && MathF.Abs(rebuiltTie.Transform.BasisY.X + 3) < 0.0001f
        && MathF.Abs(rebuiltTie.Transform.BasisZ.Z - 4) < 0.0001f,
        "UYA tie writer should encode quaternion rotation and non-uniform scale");
    var rebuiltShrubs = UyaShrubInstancesReader.Read(UyaShrubInstancesWriter.Write(
        parsedShrubs,
        [edited with { TemplateBytes = parsedShrubs.Instances[0].RawBytes }]));
    Expect(rebuiltShrubs.Instances.Single().DrawDistance == parsedShrubs.Instances[0].DrawDistance,
        "UYA shrub writer should preserve unsupported record fields");

    var quarterTurn = new UyaQuaternion(0, 0, MathF.Sin(MathF.PI / 4), MathF.Cos(MathF.PI / 4));
    var rebuiltCameras = UyaCameraInstancesReader.Read(UyaCameraInstancesWriter.Write(cameras,
        [new(0, new(100, 200, 300), quarterTurn)]));
    Expect(rebuiltCameras.Instances.Single() is { Type: 7, PvarIndex: 4 }
        && rebuiltCameras.Instances.Single().Position == new GameplayVector3(100, 200, 300)
        && MathF.Abs(rebuiltCameras.Instances.Single().Rotation.Z - MathF.PI / 2) < 0.0001f,
        "UYA camera writer should update transforms and preserve other fields");
    var rebuiltSounds = UyaSoundInstancesReader.Read(UyaSoundInstancesWriter.Write(sounds,
        [new(0, new(400, 500, 600), quarterTurn, new(2, 3, 4))]));
    var rebuiltSound = rebuiltSounds.Instances.Single();
    Expect(rebuiltSound is { ClassId: 8, MissionClass: 9, PvarIndex: 5, Range: 64 }
        && rebuiltSound.Matrix[12] == 400 && rebuiltSound.Matrix[13] == 500 && rebuiltSound.Matrix[14] == 600
        && MathF.Abs(rebuiltSound.Rotation.Z - MathF.PI / 2) < 0.0001f,
        "UYA sound writer should update transforms and preserve other fields");
}

static void ValidateGameplayGeometryParsing()
{
    var cuboidBytes = new byte[0x90];
    WriteInt32(cuboidBytes, 0, 1);
    WriteSingle(cuboidBytes, 0x10, 1);
    WriteSingle(cuboidBytes, 0x4c, 4);
    WriteSingle(cuboidBytes, 0x50, 5);
    WriteSingle(cuboidBytes, 0x80, 0.25f);
    WriteSingle(cuboidBytes, 0x84, 0.5f);
    WriteSingle(cuboidBytes, 0x88, 0.75f);

    var splineBytes = new byte[0x50];
    WriteInt32(splineBytes, 0, 1);
    WriteInt32(splineBytes, 4, 0x20);
    WriteInt32(splineBytes, 8, 0x30);
    WriteInt32(splineBytes, 0x10, 0);
    WriteInt32(splineBytes, 0x20, 2);
    WriteSingle(splineBytes, 0x30, 1);
    WriteSingle(splineBytes, 0x34, 2);
    WriteSingle(splineBytes, 0x38, 3);
    WriteSingle(splineBytes, 0x3c, 4);
    WriteSingle(splineBytes, 0x40, 5);
    WriteSingle(splineBytes, 0x44, 6);
    WriteSingle(splineBytes, 0x48, 7);
    WriteSingle(splineBytes, 0x4c, 8);

    var grindPathBytes = new byte[0x70];
    WriteInt32(grindPathBytes, 0, 1);
    WriteInt32(grindPathBytes, 4, 0x40);
    WriteInt32(grindPathBytes, 8, 0x30);
    WriteSingle(grindPathBytes, 0x10, 10);
    WriteSingle(grindPathBytes, 0x1c, 40);
    WriteInt32(grindPathBytes, 0x20, 11);
    WriteInt32(grindPathBytes, 0x24, 1);
    WriteInt32(grindPathBytes, 0x28, 2);
    WriteInt32(grindPathBytes, 0x30, 0);
    WriteInt32(grindPathBytes, 0x40, 2);
    WriteSingle(grindPathBytes, 0x50, 1);
    WriteSingle(grindPathBytes, 0x5c, 4);
    WriteSingle(grindPathBytes, 0x60, 5);
    WriteSingle(grindPathBytes, 0x6c, 8);

    var areaBytes = new byte[0x5c];
    WriteInt32(areaBytes, 0, areaBytes.Length - 4);
    WriteInt32(areaBytes, 4, 1);
    WriteInt32(areaBytes, 8, 0x50);
    WriteInt32(areaBytes, 0x0c, 0x54);
    WriteSingle(areaBytes, 0x24, 10);
    WriteSingle(areaBytes, 0x28, 20);
    WriteSingle(areaBytes, 0x2c, 30);
    WriteSingle(areaBytes, 0x30, 40);
    WriteInt16(areaBytes, 0x34, 1);
    WriteInt16(areaBytes, 0x36, 1);
    WriteInt16(areaBytes, 0x3e, 12);
    WriteInt32(areaBytes, 0x54, 7);
    WriteInt32(areaBytes, 0x58, 9);

    var geometries = new[]
    {
        (Game: "DL", Value: DlGameplayBlockReader.ReadCore(BuildGameplayData(
            DlGameplayLayout.CoreHeaderSize,
            (0x4c, cuboidBytes),
            (0x50, cuboidBytes),
            (0x54, cuboidBytes),
            (0x58, cuboidBytes),
            (0x5c, splineBytes),
            (0x60, grindPathBytes),
            (0x74, areaBytes))).Geometry),
        (Game: "UYA", Value: UyaGameplayBlockReader.ReadCore(BuildGameplayData(
            UyaGameplayLayout.CoreHeaderSize,
            (0x68, cuboidBytes),
            (0x6c, cuboidBytes),
            (0x70, cuboidBytes),
            (0x74, cuboidBytes),
            (0x78, splineBytes),
            (0x7c, grindPathBytes),
            (0x98, areaBytes))).Geometry),
        (Game: "GC", Value: UyaGameplayBlockReader.ReadCore(BuildGameplayData(
            GcGameplayLayout.CoreHeaderSize,
            (0x68, cuboidBytes),
            (0x6c, cuboidBytes),
            (0x70, cuboidBytes),
            (0x74, cuboidBytes),
            (0x78, splineBytes),
            (0x7c, grindPathBytes),
            (0x98, areaBytes)), GcGameplayLayout.Core).Geometry)
    };

    foreach (var geometry in geometries)
    {
        var cuboid = geometry.Value.Cuboids.Single();
        Expect(cuboid.Matrix[15] == 4 && cuboid.InverseRotationMatrix[0] == 5 && cuboid.Rotation.Z == 0.75f, $"{geometry.Game} cuboid should be parsed");
        Expect(geometry.Value.Spheres.Single().Matrix[15] == 4, $"{geometry.Game} sphere should be parsed");
        Expect(geometry.Value.Cylinders.Single().InverseRotationMatrix[0] == 5, $"{geometry.Game} cylinder should be parsed");
        Expect(geometry.Value.Pills.Single().Rotation.Z == 0.75f, $"{geometry.Game} pill should be parsed");
        Expect(geometry.Value.Splines.Single().Points[1].W == 8, $"{geometry.Game} spline points should be parsed");
        var grindPath = geometry.Value.GrindPaths.Single();
        Expect(grindPath.BoundingSphere.W == 40 && grindPath.Unknown4 == 11
            && grindPath.Wrap == 1 && grindPath.Inactive == 2 && grindPath.Points[1].W == 8,
            $"{geometry.Game} grind path should be parsed");
        Expect(geometry.Value.Areas.Single().SplineIndices.SequenceEqual([7]), $"{geometry.Game} area spline links should be parsed");
        Expect(geometry.Value.Areas.Single().CuboidIndices.SequenceEqual([9]), $"{geometry.Game} area cuboid links should be parsed");
    }

    cuboidBytes[0x8c] = 0x7f;
    var rebuiltCuboids = GameplayGeometryReader.ReadCuboids(UyaShapeInstancesWriter.WriteCuboids(
        cuboidBytes,
        [new(0, new(100, 200, 300), new(0, 0, 0, 1), new(2, 3, 4))]));
    var rebuiltCuboid = rebuiltCuboids.Single();
    Expect(rebuiltCuboid.Matrix[0] == 2 && rebuiltCuboid.Matrix[5] == 3 && rebuiltCuboid.Matrix[10] == 4
        && rebuiltCuboid.Matrix[12] == 100 && rebuiltCuboid.Matrix[13] == 200 && rebuiltCuboid.Matrix[14] == 300,
        "UYA shape writer should update the native matrix");
    Expect(MathF.Abs(rebuiltCuboid.InverseRotationMatrix[0] - 0.5f) < 0.0001f
        && MathF.Abs(rebuiltCuboid.InverseRotationMatrix[5] - (1f / 3f)) < 0.0001f
        && MathF.Abs(rebuiltCuboid.InverseRotationMatrix[10] - 0.25f) < 0.0001f
        && UyaShapeInstancesWriter.WriteCuboids(cuboidBytes,
            [new(0, new(100, 200, 300), new(0, 0, 0, 1), new(2, 3, 4))])[0x8c] == 0x7f,
        "UYA shape writer should update inverse rotation and preserve unknown bytes");

    splineBytes[0x2c] = 0x7f;
    var rebuiltSplineBytes = UyaSplineInstancesWriter.Write(splineBytes,
        [new(0,
            [new(1, 2, 3, 9), new(5, 6, 7, 10), new(9, 10, 11, 12)],
            new(10, 20, 30), new(0, 0, 0, 1), new(2, 3, 4))]);
    var rebuiltSpline = GameplayGeometryReader.ReadSplines(rebuiltSplineBytes).Single();
    Expect(rebuiltSpline.Points[0] == new GameplayVector4(12, 26, 42, 9)
        && rebuiltSpline.Points[1] == new GameplayVector4(20, 38, 58, 10)
        && rebuiltSpline.Points[2] == new GameplayVector4(28, 50, 74, 12)
        && rebuiltSplineBytes[0x2c] == 0x7f,
        "UYA spline writer should rebuild editable points while preserving record padding");
}

static void ValidateUyaGameplayLightingParsing()
{
    var directional = new byte[0x50];
    WriteInt32(directional, 0, 1);
    WriteSingle(directional, 0x10, 0.25f);
    WriteSingle(directional, 0x2c, -1);
    WriteSingle(directional, 0x40, 0.75f);

    var point = new byte[0x820];
    WriteInt32(point, 0, 1);
    point[0x10 + 16] = 1;
    point[0x10 + 32] = 1;
    point[0x410] = 1;
    point[0x410 + 16] = 1;
    WriteUInt16(point, 0x810, 32 * 64);
    WriteUInt16(point, 0x812, 16 * 64);
    WriteUInt16(point, 0x814, 8 * 64);
    WriteUInt16(point, 0x816, 8 * 64);
    WriteUInt16(point, 0x818, 0xffff);
    WriteUInt16(point, 0x81a, 0x8000);
    WriteUInt16(point, 0x81c, 0x4000);

    var sample = new byte[0x30];
    WriteInt32(sample, 0, 1);
    WriteInt32(sample, 0x10, 3);
    WriteInt16(sample, 0x14, 40);
    WriteInt16(sample, 0x16, 80);
    WriteInt16(sample, 0x18, 120);
    WriteInt16(sample, 0x1a, 7);
    WriteInt16(sample, 0x1c, 9);
    sample[0x20] = 10;
    sample[0x21] = 20;
    sample[0x22] = 30;
    sample[0x23] = 4;
    sample[0x27] = 40;
    sample[0x28] = 50;
    sample[0x29] = 60;
    WriteInt16(sample, 0x2a, 100);
    WriteInt16(sample, 0x2c, 200);

    var transition = new byte[0xa0];
    WriteInt32(transition, 0, 1);
    WriteSingle(transition, 0x10, 1);
    WriteSingle(transition, 0x1c, 4);
    WriteSingle(transition, 0x20, 1);
    WriteSingle(transition, 0x34, 1);
    WriteSingle(transition, 0x48, 1);
    WriteSingle(transition, 0x5c, 1);
    transition[0x60] = 1;
    transition[0x64] = 2;
    WriteInt32(transition, 0x68, 5);
    WriteInt32(transition, 0x6c, 6);
    WriteInt32(transition, 0x70, 3);
    WriteSingle(transition, 0x7c, 10);
    WriteSingle(transition, 0x98, 80);

    var ties = new byte[0x70];
    WriteInt32(ties, 0, 1);
    WriteInt32(ties, 0x60, 12);
    var ambient = new byte[10];
    WriteInt16(ambient, 0, 0);
    WriteInt16(ambient, 2, 2);
    ambient[4] = 0x11;
    ambient[5] = 0x22;
    ambient[6] = 0x33;
    ambient[7] = 0x44;
    WriteInt16(ambient, 8, -1);

    var gameplay = UyaGameplayBlockReader.ReadCore(BuildGameplayData(
        UyaGameplayLayout.CoreHeaderSize,
        (0x04, directional), (0x34, ties), (0x80, point),
        (0x84, transition), (0x8c, sample), (0x94, ambient)));
    var lighting = gameplay.Lighting;
    Expect(lighting.DirectionalLights.Single().TopColor.X == 0.25f
        && lighting.DirectionalLights.Single().InverseDirection.X == 0.75f,
        "UYA directional lights should be parsed");
    Expect(lighting.PointLights.MasksMatchDerived
        && lighting.PointLights.Lights.Single().Position == new GameplayVector3(32, 16, 8)
        && lighting.PointLights.Lights.Single().ColorR == 0xffff,
        "UYA point lights and masks should be parsed");
    Expect(lighting.EnvironmentSamplePoints.Single().Position == new GameplayVector3(10, 20, 30)
        && lighting.EnvironmentSamplePoints.Single().FogColor == new UyaRgb24(40, 50, 60),
        "UYA environment sample points should be parsed");
    Expect(lighting.EnvironmentTransitions.Single().BoundingSphere.W == 4
        && lighting.EnvironmentTransitions.Single().Flags == 3
        && lighting.EnvironmentTransitions.Single().FogFarIntensity2 == 80,
        "UYA environment transitions should be parsed");
    Expect(gameplay.Blocks.Single(block => block.TieInstances is not null)
        .TieInstances!.Instances.Single().DirectionalLights == 12,
        "UYA tie directional-light selector should be parsed");
    Expect(lighting.TieAmbientRgbas.Single().SequenceEqual(new byte[] { 0x11, 0x22, 0x33, 0x44 }),
        "UYA tie ambient words should be associated by source index");
    var rebuiltAmbient = UyaTieAmbientRgbasWriter.Write([[], [0x11, 0x22], [], [0x33, 0x44, 0x55, 0x66]]);
    var rebuiltAmbientValues = UyaGameplayLightingReader.ReadTieAmbientRgbas(rebuiltAmbient, 4);
    Expect(rebuiltAmbientValues[0].Length == 0
        && rebuiltAmbientValues[1].SequenceEqual(new byte[] { 0x11, 0x22 })
        && rebuiltAmbientValues[2].Length == 0
        && rebuiltAmbientValues[3].SequenceEqual(new byte[] { 0x33, 0x44, 0x55, 0x66 }),
        "UYA tie ambient writer should preserve sparse entries and target indices");

    var groups = new byte[0x30];
    WriteInt32(groups, 0, 1);
    WriteInt32(groups, 4, 0x10);
    WriteInt32(groups, 0x10, 0);
    WriteUInt16(groups, 0x20, 0);
    WriteUInt16(groups, 0x22, 1);
    WriteUInt16(groups, 0x24, 0x8002);
    var rebuiltGroups = UyaTieGroupsReader.Read(UyaTieGroupsWriter.Remap(groups, [0, 0, 2]));
    Expect(rebuiltGroups.Groups.Single().SequenceEqual(new[] { 0, 1, 2 }),
        "UYA tie group writer should duplicate and renumber tie references");

    var occlusion = new byte[0x30];
    WriteInt32(occlusion, 0, 1);
    WriteInt32(occlusion, 4, 3);
    WriteInt32(occlusion, 0x10, 10);
    WriteInt32(occlusion, 0x14, 20);
    var occlusionIds = new[] { 42, 40, 41 };
    for (var index = 0; index < 3; index++)
    {
        WriteInt32(occlusion, 0x18 + index * 8, 30 + index);
        WriteInt32(occlusion, 0x1c + index * 8, occlusionIds[index]);
    }
    var rebuiltOcclusion = UyaOcclusionMappingsReader.Read(
        UyaOcclusionMappingsWriter.RemapTies(occlusion, [40, 40, 42]));
    Expect(rebuiltOcclusion.Ties.Select(value => (value.BitIndex, value.OcclusionId))
            .SequenceEqual(new[] { (30, 42), (31, 40), (31, 40) }),
        "UYA occlusion writer should preserve source ordering while duplicating tie IDs");
}

static void ValidateUyaAssetRenderPackageBuild()
{
    var rawAssetBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 };
    var compressedAssetBytes = CreateLiteralWad(rawAssetBytes);
    var files = DlLevelWadRenderPackageBuilder.BuildAssetFiles(
        GameId.UYA,
        levelIndex: 41,
        headerBytes: new byte[0xc0],
        paletteBytes: [],
        assetBytes: compressedAssetBytes);
    var byPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);

    Expect(byPath.ContainsKey("assets/manifest.json"), "UYA asset render package should include an asset manifest");
    Expect(byPath.ContainsKey("assets/render_manifest.json"), "UYA asset render package should include a render manifest");

    using var assetManifest = JsonDocument.Parse(byPath["assets/manifest.json"].Bytes);
    Expect(assetManifest.RootElement.GetProperty("Game").GetString() == "UYA", "UYA asset manifest should preserve the game id");
    Expect(!assetManifest.RootElement.GetProperty("TextureIsSwizzled").GetBoolean(), "UYA asset textures should be exported without swizzle");
    Expect(assetManifest.RootElement.GetProperty("GltfExportCount").GetInt32() == 0, "empty UYA asset package should not report written glTFs");

    using var renderManifest = JsonDocument.Parse(byPath["assets/render_manifest.json"].Bytes);
    Expect(!renderManifest.RootElement.GetProperty("TextureIsSwizzled").GetBoolean(), "UYA render manifest should report unswizzled asset textures");
    Expect(renderManifest.RootElement.GetProperty("AssetWadWasCompressed").GetBoolean(), "UYA asset render package should decompress compressed asset WAD input");
    Expect(
        renderManifest.RootElement.GetProperty("AssetWadPayloadLength").GetInt32() == rawAssetBytes.Length,
        "UYA asset render package should report the decompressed asset WAD length");
}

static void ValidateChunkTfragAssetRenderPackageWhenAvailable()
{
    var tfragPath = Path.Combine("test-assets", "tfrags", "DL", "level1", "terrain", "terrain.bin");
    if (!File.Exists(tfragPath))
    {
        return;
    }

    var chunkWad = CreateChunkWad(File.ReadAllBytes(tfragPath));
    var files = DlLevelWadRenderPackageBuilder.BuildAssetFiles(
        GameId.DL,
        levelIndex: 1,
        headerBytes: new byte[0xc0],
        paletteBytes: [],
        assetBytes: [],
        options: DlLevelWadRenderPackageBuildOptions.Browser,
        chunkWads: new Dictionary<int, byte[]>
        {
            [0] = chunkWad,
            [1] = chunkWad
        });
    var byPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);

    Expect(!byPath.ContainsKey("assets/tfrag/chunks/chunk0/tfrag.gltf"), "chunk0 tfrag should not be exported");
    Expect(byPath.ContainsKey("assets/tfrag/chunks/chunk1/tfrag.gltf"), "chunk1 tfrag glTF should be exported");
    Expect(byPath.ContainsKey("assets/tfrag/chunks/chunk1/tfrag.buffer.bin"), "chunk1 tfrag buffer should be exported");
    Expect(!byPath.ContainsKey("assets/tfrag/chunks/chunk1/tfrag.bin"), "browser package should omit chunk tfrag source bytes");

    using var assetManifest = JsonDocument.Parse(byPath["assets/manifest.json"].Bytes);
    var chunkRoute = assetManifest.RootElement
        .GetProperty("GltfExports")
        .EnumerateArray()
        .SingleOrDefault(entry =>
            entry.GetProperty("Family").GetString() == "tfrag"
            && entry.GetProperty("ModelId").ValueKind == JsonValueKind.Number
            && entry.GetProperty("ModelId").GetInt32() == 1);
    Expect(chunkRoute.ValueKind == JsonValueKind.Object, "asset manifest should contain the chunk1 tfrag route");
    Expect(chunkRoute.GetProperty("Status").GetString() == "written", "chunk1 tfrag route should be written");
    Expect(
        chunkRoute.GetProperty("GltfPath").GetString() == "tfrag/chunks/chunk1/tfrag.gltf",
        "chunk1 tfrag route should point at the chunk glTF");
}

static void ValidateChunkTfragWadReaderWhenAvailable()
{
    var chunkPath = Path.Combine(
        "test-assets",
        "extractions_uya",
        "level04_iso_world01",
        "level_wad",
        "chunks",
        "chunk1.wad");
    if (!File.Exists(chunkPath))
    {
        return;
    }

    var terrainBytes = TfragChunkWadReader.ReadTerrainPayload(File.ReadAllBytes(chunkPath));
    var terrain = TfragTerrainReader.Read(terrainBytes);
    Expect(terrain.Chunks.Count > 0, "chunk tfrag WAD reader should decode the first chunk payload as terrain");
}

static void ValidateLooseLevelWadRenderPackageWhenAvailable()
{
    var wadPath = Environment.GetEnvironmentVariable("RATCHET_PS2_DL_LEVEL_WAD")
        ?? "/tmp/ratchet-dl-wad-realdata-44-v2/level44.wad";
    if (!File.Exists(wadPath))
    {
        return;
    }

    var wadBytes = File.ReadAllBytes(wadPath);
    var levelWad = DlLevelWadReader.ReadLevelWad(wadBytes);
    var renderPackage = DlLevelWadRenderPackageBuilder.BuildPacked(
        wadBytes,
        DlLevelWadRenderPackageBuildOptions.Browser);
    var entries = renderPackage.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);

    Expect(entries.ContainsKey("manifest.json"), "render package should include the root viewer manifest");
    Expect(entries.ContainsKey("assets/manifest.json"), "render package should include the asset viewer manifest");
    Expect(entries.ContainsKey("world/manifest.json"), "render package should include the world viewer manifest");
    Expect(entries.ContainsKey("assets/tfrag/tfrag.gltf"), "render package should include the terrain glTF");
    Expect(entries.ContainsKey("assets/tfrag/tfrag.buffer.bin"), "render package should include the terrain glTF buffer");
    Expect(entries.ContainsKey("world/lighting/directional_lights.bin"), "render package should include directional light sidecars");
    Expect(!entries.ContainsKey("assets/tfrag/tfrag.bin"), "browser render package should omit source terrain bytes");
    Expect(
        entries.Keys.All(path => !path.EndsWith(".diagnostics.json", StringComparison.Ordinal)),
        "browser render package should omit glTF diagnostics");
    Expect(
        DlLevelWadRenderPackageBuildOptions.Browser.IncludeMissionMobys,
        "browser render packages should include mission mobys when present");
    if (levelWad.GameplayMissionData.Any(block => !block.IsEmpty))
    {
        Expect(
            entries.Keys.Any(path => path.StartsWith("missions/", StringComparison.Ordinal)),
            "browser render package should include available mission mobys");
        Expect(
            entries.Keys.Any(path => path.StartsWith("missions/mission_", StringComparison.Ordinal)
                && path.EndsWith("/gameplay.bin", StringComparison.Ordinal)),
            "browser render package should include available mission gameplay");
    }
    Expect(
        entries.Keys.All(path =>
            !path.EndsWith("/tie.bin", StringComparison.Ordinal)
            && !path.EndsWith("/moby.bin", StringComparison.Ordinal)
            && !path.EndsWith("/shrub.bin", StringComparison.Ordinal)
            && !path.EndsWith("/tie.json", StringComparison.Ordinal)
            && !path.EndsWith("/moby.json", StringComparison.Ordinal)
            && !path.EndsWith("/shrub.json", StringComparison.Ordinal)),
        "browser render package should omit source moby, tie, and shrub sidecars");

    using var rootManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["manifest.json"]));
    var writtenMobys = rootManifest.RootElement.GetProperty("Mobys").EnumerateArray()
        .Where(entry => entry.GetProperty("Status").GetString() == "written")
        .ToArray();
    Expect(
        writtenMobys.Select(entry => entry.GetProperty("ClassId").GetInt32()).Distinct().Count() == writtenMobys.Length,
        "render package should contain only one written moby per class");
    var performanceTimings = rootManifest.RootElement.GetProperty("PerformanceTimings").EnumerateArray().ToArray();
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.assets.tfrag"),
        "render package manifest should include top-level terrain timing");
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.assets.mobys"),
        "render package manifest should include top-level moby timing");
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.assets.fx-textures"),
        "render package manifest should include FX texture timing");
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.tfrag.decode"),
        "render package manifest should include terrain exporter subphase timing");
    using (var tfragGltf = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["assets/tfrag/tfrag.gltf"])))
    {
        Expect(
            tfragGltf.RootElement.GetProperty("nodes").EnumerateArray().All(node =>
                !node.TryGetProperty("name", out var name)
                || name.GetString()?.Contains("lod_1", StringComparison.Ordinal) != true
                && name.GetString()?.Contains("lod_2", StringComparison.Ordinal) != true),
            "browser render package terrain glTF should include only LOD0");
    }

    using var assetManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["assets/manifest.json"]));
    var assetHeader = assetManifest.RootElement.GetProperty("Header");
    var gltfExports = assetManifest.RootElement.GetProperty("GltfExports").EnumerateArray().ToArray();
    Expect(
        gltfExports.Any(entry =>
            entry.GetProperty("Family").GetString() == "tfrag"
            && entry.GetProperty("Status").GetString() == "written"),
        "render package asset manifest should contain a written tfrag export");

    if (assetHeader.GetProperty("MobyModelCount").GetInt32() > 0)
    {
        Expect(
            gltfExports.Any(entry =>
                entry.GetProperty("Family").GetString() == "moby"
                && entry.GetProperty("Status").GetString() == "written"),
            "render package asset manifest should contain a written moby export");
        Expect(
            entries.Keys.Any(path =>
                path.StartsWith("assets/moby/", StringComparison.Ordinal)
                && path.EndsWith("/moby.gltf", StringComparison.Ordinal)),
            "render package should include moby glTF files");
        var mobyGltfPath = entries.Keys.First(path =>
            path.StartsWith("assets/moby/", StringComparison.Ordinal)
            && path.EndsWith("/moby.gltf", StringComparison.Ordinal));
        using var mobyGltf = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries[mobyGltfPath]));
        Expect(
            !mobyGltf.RootElement.GetProperty("nodes").EnumerateArray().Any(node =>
                node.TryGetProperty("name", out var name)
                && (name.GetString()?.Contains("low_lod", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("far_lod", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("mesh_type_2", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("LowLod", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("FarLod", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("MeshType2", StringComparison.Ordinal) == true)),
            "browser render package moby glTF should include only LOD0 render mesh groups");
    }

    var fxTextureCount = assetHeader.GetProperty("FxTextureCount").GetInt32();
    if (fxTextureCount > 0)
    {
        Expect(entries.ContainsKey("assets/fx/manifest.json"), "render package should include the FX texture manifest");
        Expect(
            entries.Keys.Count(path =>
                path.StartsWith("assets/fx/textures/", StringComparison.Ordinal)
                && path.EndsWith(".png", StringComparison.Ordinal)) == fxTextureCount,
            "render package should include one PNG per FX texture");
    }

    if (gltfExports.Any(entry =>
        entry.GetProperty("Family").GetString() == "tie"
        && entry.GetProperty("Status").GetString() == "written"))
    {
        Expect(
            performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.tie.document"),
            "render package manifest should include aggregated tie document timing when ties are written");
    }
}

static void ValidateUyaLooseLevelWadRenderPackageWhenAvailable()
{
    var wadPath = Environment.GetEnvironmentVariable("RATCHET_PS2_UYA_LEVEL_WAD")
        ?? Path.Combine("test-assets", "extractions_uya", "level41.wad");
    if (!File.Exists(wadPath))
    {
        return;
    }

    var package = UyaLevelWadUnpacker.Unpack(File.ReadAllBytes(wadPath));
    var renderPackage = UyaLevelWadRenderPackageBuilder.BuildPacked(
        package.LevelWad.Level,
        package.Files,
        assetFiles => DlLevelWadRenderPackageBuilder.BuildAssetFiles(
            GameId.UYA,
            package.LevelWad.Level,
            assetFiles.HeaderBytes,
            assetFiles.PaletteBytes,
            assetFiles.AssetWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser));
    var entries = renderPackage.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);

    Expect(entries.ContainsKey("manifest.json"), "UYA render package should include the root viewer manifest");
    Expect(entries.ContainsKey("assets/manifest.json"), "UYA render package should include the asset viewer manifest");
    Expect(entries.ContainsKey("world/manifest.json"), "UYA render package should include the world viewer manifest");
    Expect(entries.ContainsKey("world/lighting/directional_lights.bin"), "UYA render package should expose directional lights through the world path");
    Expect(entries.ContainsKey("world/tie/instances.bin"), "UYA render package should expose tie instances through the world path");
    Expect(entries.ContainsKey("world/shrub/instances.bin"), "UYA render package should expose shrub instances through the world path");

    using var rootManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["manifest.json"]));
    Expect(rootManifest.RootElement.GetProperty("Game").GetString() == "UYA", "UYA render package root manifest should preserve the game id");

    using var assetManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["assets/manifest.json"]));
    Expect(!assetManifest.RootElement.GetProperty("TextureIsSwizzled").GetBoolean(), "UYA render package should keep asset textures unswizzled");
}

static void ValidateLooseLevelWadFailures()
{
    const int levelIndex = 4;
    const int headerSector = 20;
    const int payloadBaseSector = 60;

    var negativeBlockWad = CreateSyntheticLooseLevelWad(payloadBaseSector, negativeBlock: true);
    var negativeBlockIso = CreateSyntheticIso(levelIndex, headerSector, payloadBaseSector, negativeBlockWad);
    using (var stream = new MemoryStream(negativeBlockIso, writable: false))
    {
        ExpectThrows<InvalidDataException>(() => DlLooseLevelWadExtractor.ExtractPrimary(stream, levelIndex));
    }

    var outOfRangePayloadBaseSector = 2000;
    var outOfRangeWad = CreateSyntheticLooseLevelWad(outOfRangePayloadBaseSector);
    var outOfRangeIso = CreateSyntheticIso(
        levelIndex,
        headerSector,
        outOfRangePayloadBaseSector,
        outOfRangeWad,
        includePayloads: false);
    using (var stream = new MemoryStream(outOfRangeIso, writable: false))
    {
        ExpectThrows<InvalidDataException>(() => DlLooseLevelWadExtractor.ExtractPrimary(stream, levelIndex));
    }

    var badHeader = CreateSyntheticLooseLevelWad(payloadBaseSector);
    WriteInt32(badHeader, 0x00, DlLevelConstants.LevelWadHeaderSize - 1);
    ExpectThrows<InvalidDataException>(() => DlLevelWadUnpacker.Unpack(badHeader));
}

static byte[] ReadPackedEntryBytes(PackedFilePackage package, PackedFileEntry entry)
{
    return package.PackedBytes.AsSpan(entry.Offset, entry.Length).ToArray();
}

static void ValidateMissionPlaceholderDetection()
{
    var placeholder = new byte[DlLevelConstants.SectorSize];
    WriteInt32(placeholder, 0x00, -1);
    WriteInt32(placeholder, 0x04, 0);
    WriteInt32(placeholder, 0x08, -1);
    WriteInt32(placeholder, 0x0c, 0);

    Expect(DlMissionDataReader.IsPlaceholderMissionData(placeholder), "mission placeholder sentinel should be detected");
    Expect(!DlMissionDataReader.IsPlaceholderMissionData(placeholder[..(DlLevelConstants.SectorSize - 1)]), "mission placeholder should require one sector");

    var nonZeroPayload = placeholder.ToArray();
    nonZeroPayload[0x20] = 1;
    Expect(!DlMissionDataReader.IsPlaceholderMissionData(nonZeroPayload), "mission placeholder should reject non-zero payload bytes");

    var realMissionHeader = new byte[DlLevelConstants.SectorSize];
    WriteInt32(realMissionHeader, 0x00, 0x40);
    WriteInt32(realMissionHeader, 0x04, 0x20);
    WriteInt32(realMissionHeader, 0x08, 0x60);
    WriteInt32(realMissionHeader, 0x0c, 0x10);
    Expect(!DlMissionDataReader.IsPlaceholderMissionData(realMissionHeader), "mission placeholder should reject real mission table headers");
}

static void ValidateMissionMobyBankParsing()
{
    var pif = PifWriter.Write(PifWriter.CreateIndexed8(
        2,
        2,
        new byte[0x400],
        [0, 1, 2, 3]));
    var bank = new byte[0x50 + pif.Length];
    WriteInt32(bank, 0x00, 1);
    WriteInt32(bank, 0x10, 0x24f9);
    WriteInt32(bank, 0x14, 0x30);
    WriteInt32(bank, 0x18, 0x40);
    for (var i = 0; i < 0x10; i++)
    {
        bank[0x30 + i] = (byte)i;
    }
    WriteInt32(bank, 0x40, 1);
    WriteInt32(bank, 0x44, 0x10);
    pif.CopyTo(bank, 0x50);

    var mission = new byte[0x80 + bank.Length];
    WriteInt32(mission, 0x00, 0x40);
    WriteInt32(mission, 0x08, 0x80);
    WriteInt32(mission, 0x0c, bank.Length);
    bank.CopyTo(mission, 0x80);

    var mobys = DlMissionMobyBankReader.Read(DlMissionDataReader.ReadClasses(mission));
    Expect(mobys.Count == 1, "mission moby bank should read its definition count");
    Expect(mobys[0].Definition.ClassId == 0x24f9, "mission moby bank should read the class id");
    Expect(mobys[0].ModelBytes.Length == 0x10, "mission moby bank should slice model bytes at the texture boundary");
    Expect(mobys[0].PifTextures.Count == 1, "mission moby bank should read embedded PIF textures");
    Expect(mobys[0].PifTextures[0].SequenceEqual(pif), "mission moby bank should preserve the complete PIF payload");
}

static void ValidateLevelSceneWadEmptyDetection()
{
    var sceneWadBytes = new byte[DlLevelConstants.SectorSize * DlLevelConstants.LevelSceneWadHeaderSectorCount];
    WriteInt32(sceneWadBytes, 0x00, DlLevelConstants.LevelSceneWadHeaderSize);
    WriteInt32(sceneWadBytes, 0x04, 0x1234);

    var sceneWad = DlLevelWadReader.ReadLevelSceneWad(sceneWadBytes);
    Expect(DlLevelWadReader.IsHeaderOnlyLevelSceneWad(sceneWadBytes, sceneWad), "header-only level scene WAD should be detected");
    Expect(sceneWad.Scenes.All(DlLevelWadReader.IsEmptyScene), "zeroed scene records should be treated as empty");

    var nonZeroPadding = sceneWadBytes.ToArray();
    nonZeroPadding[DlLevelConstants.LevelSceneWadHeaderSize] = 1;
    Expect(
        !DlLevelWadReader.IsHeaderOnlyLevelSceneWad(nonZeroPadding, DlLevelWadReader.ReadLevelSceneWad(nonZeroPadding)),
        "level scene WAD with non-zero padding should not be treated as header-only");

    var realSpeechOffset = sceneWadBytes.ToArray();
    WriteInt32(realSpeechOffset, 0x08, 5);
    var speechSceneWad = DlLevelWadReader.ReadLevelSceneWad(realSpeechOffset);
    Expect(!DlLevelWadReader.IsEmptyScene(speechSceneWad.Scenes[0]), "scene speech offsets should make a scene non-empty");
    Expect(!DlLevelWadReader.IsHeaderOnlyLevelSceneWad(realSpeechOffset, speechSceneWad), "level scene WAD with scene metadata should not be treated as header-only");

    var realSubtitles = sceneWadBytes.ToArray();
    WriteFileBlock(realSubtitles, 0x10, new DlFileBlock(2, 1));
    var subtitlesSceneWad = DlLevelWadReader.ReadLevelSceneWad(realSubtitles);
    Expect(!DlLevelWadReader.IsEmptyScene(subtitlesSceneWad.Scenes[0]), "scene subtitle fileblocks should make a scene non-empty");
    Expect(!DlLevelWadReader.IsHeaderOnlyLevelSceneWad(realSubtitles, subtitlesSceneWad), "level scene WAD with subtitle metadata should not be treated as header-only");
}

static void ValidateCoreLevelSegments()
{
    var uncompressed = new byte[] { 1, 2, 3, 4 };
    var decompressed = Enumerable.Range(0, 0x400).Select(value => (byte)(value & 0xff)).ToArray();
    var compressed = WadCompression.Compress(decompressed);
    var coreLevelBytes = new byte[0x200 + compressed.Length];

    WriteFileBlock(coreLevelBytes, 0x10, new DlFileBlock(0x100, uncompressed.Length));
    WriteFileBlock(coreLevelBytes, 0x18, new DlFileBlock(0x180, compressed.Length));
    uncompressed.CopyTo(coreLevelBytes.AsSpan(0x100));
    compressed.CopyTo(coreLevelBytes.AsSpan(0x180));

    var segments = DlCoreLevelSegmentReader.Read(coreLevelBytes);
    var assetHeader = segments.Single(segment => segment.HeaderOffset == 0x10);
    var palette = segments.Single(segment => segment.HeaderOffset == 0x18);

    Expect(assetHeader.SemanticName == "asset_header", "segment 0x10 should be named asset_header");
    Expect(assetHeader.RawBytes.SequenceEqual(uncompressed), "uncompressed segment raw bytes should be preserved");
    Expect(assetHeader.PayloadBytes.SequenceEqual(uncompressed), "uncompressed segment payload should match raw bytes");
    Expect(!assetHeader.WasCompressedWad, "uncompressed segment should not be marked compressed");
    Expect(palette.SemanticName == "palette", "segment 0x18 should be named palette");
    Expect(palette.RawBytes.SequenceEqual(compressed), "compressed segment raw bytes should be preserved");
    Expect(palette.PayloadBytes.SequenceEqual(decompressed), "compressed segment payload should be decompressed");
    Expect(palette.WasCompressedWad, "compressed segment should be marked as compressed WAD");
}

static void ValidateWadCompression()
{
    var empty = WadCompression.CompressVerified([]);
    Expect(empty.CompressedBytes.SequenceEqual(new byte[]
    {
        0x57, 0x41, 0x44, 0x10, 0, 0, 0, 0x52, 0x41, 0x43, 0x43, 0x4c, 0x49, 0x30, 0x30, 0x31,
    }), "empty WAD compression should match the stable golden vector");
    var oneByte = WadCompression.CompressVerified([1]);
    Expect(oneByte.CompressedBytes.SequenceEqual(new byte[]
    {
        0x57, 0x41, 0x44, 0x14, 0, 0, 0, 0x52, 0x41, 0x43, 0x43, 0x4c, 0x49, 0x30, 0x30, 0x31,
        0x11, 0x01, 0x00, 0x01,
    }), "single-byte WAD compression should match the stable golden vector");

    var random = new Random(0x524143);
    foreach (var length in new[] { 0, 1, 2, 3, 17, 18, 19, 263, 264, 265, 272, 273, 274, 0x1fef, 0x1ff0, 0x1ff1 })
    {
        var bytes = new byte[length];
        random.NextBytes(bytes);
        ExpectVerifiedWadRoundTrip(bytes);
    }
    for (var index = 0; index < 40; index++)
    {
        var bytes = new byte[random.Next(0, 4097)];
        random.NextBytes(bytes);
        ExpectVerifiedWadRoundTrip(bytes);
    }

    var repetitive = Enumerable.Repeat((byte)0xA5, 0x10000).ToArray();
    var repetitiveResult = ExpectVerifiedWadRoundTrip(repetitive);
    Expect(repetitiveResult.CompressedSize < repetitiveResult.UncompressedSize,
        "repetitive WAD input should exercise match compression");
    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(
        repetitiveResult.CompressedBytes,
        new WadDecompressionOptions(MaxOutputBytes: 0x1000, MaxExpansionRatio: 1024)));
    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(
        repetitiveResult.CompressedBytes,
        new WadDecompressionOptions(MaxOutputBytes: repetitive.Length, MaxExpansionRatio: 1)));

    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(new byte[15]));
    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(CreateRawCompressedWad([0x40, 0x00])));
    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(CreateRawCompressedWad([0x01, 1, 2, 3, 4, 0x01])));
    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(CreateRawCompressedWad([0x12, 0x00, 0x00])));
    ExpectThrows<ArgumentOutOfRangeException>(() => WadCompression.Decompress(
        empty.CompressedBytes, new WadDecompressionOptions(MaxExpansionRatio: 0)));

    var invalidSize = CreateRawCompressedWad([]);
    WriteInt32(invalidSize, 3, 15);
    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(invalidSize));
    var invalidMagic = CreateRawCompressedWad([]);
    invalidMagic[0] = 0;
    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(invalidMagic));
    var oversizedDeclaration = CreateRawCompressedWad([]);
    WriteInt32(oversizedDeclaration, 3, oversizedDeclaration.Length + 1);
    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(oversizedDeclaration));
    var truncated = oneByte.CompressedBytes[..^1];
    WriteInt32(truncated, 3, truncated.Length);
    ExpectThrows<InvalidDataException>(() => WadCompression.Decompress(truncated));

    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    ExpectThrows<OperationCanceledException>(() => WadCompression.CompressVerified(repetitive, cancellationToken: cancellation.Token));
    ExpectThrows<OperationCanceledException>(() => WadCompression.Decompress(
        repetitiveResult.CompressedBytes, new WadDecompressionOptions(), cancellation.Token));
}

static WadCompressionResult ExpectVerifiedWadRoundTrip(byte[] bytes)
{
    var first = WadCompression.CompressVerified(bytes);
    var second = WadCompression.CompressVerified(bytes);
    Expect(first.CompressedBytes.SequenceEqual(second.CompressedBytes),
        "equal WAD inputs should produce deterministic compressed bytes");
    Expect(first.UncompressedSize == bytes.Length && first.CompressedSize == first.CompressedBytes.Length,
        "verified WAD compression should report exact sizes");
    Expect(first.UncompressedSha256.Length == 64 && first.CompressedSha256.Length == 64,
        "verified WAD compression should report SHA-256 values");
    Expect(WadCompression.Decompress(first.CompressedBytes).SequenceEqual(bytes),
        "verified WAD compression should decompress byte-for-byte");
    return first;
}

static byte[] CreateRawCompressedWad(byte[] packets)
{
    var bytes = new byte[0x10 + packets.Length];
    "WAD"u8.CopyTo(bytes);
    WriteInt32(bytes, 3, bytes.Length);
    packets.CopyTo(bytes.AsSpan(0x10));
    return bytes;
}

static void ValidateGameplayLevelSettingsParsing()
{
    var levelSettingsBytes = new byte[0xb8];
    WriteInt32(levelSettingsBytes, 0x00, 57);
    WriteInt32(levelSettingsBytes, 0x04, 65);
    WriteInt32(levelSettingsBytes, 0x08, 50);
    WriteInt32(levelSettingsBytes, 0x0c, 40);
    WriteInt32(levelSettingsBytes, 0x10, 50);
    WriteInt32(levelSettingsBytes, 0x14, 40);
    WriteSingle(levelSettingsBytes, 0x18, 61440);
    WriteSingle(levelSettingsBytes, 0x1c, 179200);
    WriteSingle(levelSettingsBytes, 0x20, 255);
    WriteSingle(levelSettingsBytes, 0x24, 63.75f);
    WriteSingle(levelSettingsBytes, 0x3c, 20);
    WriteSingle(levelSettingsBytes, 0x40, 20);
    WriteSingle(levelSettingsBytes, 0x44, 20);
    WriteInt32(levelSettingsBytes, 0x4c, -1);
    WriteInt32(levelSettingsBytes, 0x7c, 59);

    var gameplay = DlGameplayBlockReader.ReadCore(BuildGameplayData(
        DlGameplayBlockReader.CoreHeaderSize,
        (0x00, levelSettingsBytes),
        (0x08, [0xaa]),
        (0x0c, [0xbb]),
        (0x5c, [0xcc]),
        (0x60, [0xdd])));
    var settings = gameplay.Blocks.Single(block => block.SemanticName == "level_settings").LevelSettings;

    Expect(settings is not null, "core level_settings block should be parsed into a typed model");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "ambient_sound_instances"), "gameplay sound instances should use the ambient sound name");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "us_english_strings"), "gameplay language string blocks should use string names");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "splines"), "gameplay path blocks should use spline names");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "grind_splines"), "gameplay grind path blocks should use grind spline names");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "pad_78"), "gameplay 0x78 slot should be named padding");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "pad_7c"), "gameplay 0x7c slot should be named padding");
    Expect(settings!.BackgroundColor == new DlRgb96(57, 65, 50), "level settings background color should be parsed");
    Expect(settings.FogColor == new DlRgb96(40, 50, 40), "level settings fog color should be parsed");
    Expect(settings.FogFarDistance == 179200, "level settings fog far distance should be parsed");
    Expect(settings.ShipPosition == new DlVector3(20, 20, 20), "level settings ship position should be parsed");
    Expect(settings.ShipPath == -1, "level settings ship path should be parsed");
    Expect(settings.ChunkPlanes.Count == 0, "empty level settings chunk plane terminator should be skipped");
    Expect(settings.CoreSoundsCount == 59, "level settings core sound count should be parsed");
    Expect(settings.ThirdPartCount == 0, "level settings DL third part count should be parsed");
    Expect(settings.FifthPart is not null, "level settings DL fifth part should be parsed");
    Expect(settings.DebugAttackDamage.Length == 0, "empty level settings debug attack damage array should be parsed");
}

static void ValidateGameplayMobyInstancesParsing()
{
    var mobyBytes = new byte[DlMobyInstancesReader.HeaderSize + DlMobyInstancesReader.RecordSize];
    WriteInt32(mobyBytes, 0x00, 1);
    WriteInt32(mobyBytes, 0x04, 400);

    const int mobyOffset = DlMobyInstancesReader.HeaderSize;
    WriteInt32(mobyBytes, mobyOffset, DlMobyInstancesReader.RecordSize);
    WriteInt32(mobyBytes, mobyOffset + 0x04, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x08, 0x78);
    WriteInt32(mobyBytes, mobyOffset + 0x0c, 4);
    WriteInt32(mobyBytes, mobyOffset + 0x10, 0x0b37);
    WriteSingle(mobyBytes, mobyOffset + 0x14, 1.5f);
    WriteInt32(mobyBytes, mobyOffset + 0x18, 64);
    WriteInt32(mobyBytes, mobyOffset + 0x1c, 80);
    WriteInt32(mobyBytes, mobyOffset + 0x20, 32);
    WriteInt32(mobyBytes, mobyOffset + 0x24, 64);
    WriteSingle(mobyBytes, mobyOffset + 0x28, 10);
    WriteSingle(mobyBytes, mobyOffset + 0x2c, 20);
    WriteSingle(mobyBytes, mobyOffset + 0x30, 30);
    WriteSingle(mobyBytes, mobyOffset + 0x34, 0.25f);
    WriteSingle(mobyBytes, mobyOffset + 0x38, 0.5f);
    WriteSingle(mobyBytes, mobyOffset + 0x3c, 0.75f);
    WriteInt32(mobyBytes, mobyOffset + 0x40, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x44, 1);
    WriteSingle(mobyBytes, mobyOffset + 0x48, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x4c, 1);
    WriteInt32(mobyBytes, mobyOffset + 0x50, 10);
    WriteInt32(mobyBytes, mobyOffset + 0x54, 1);
    WriteInt32(mobyBytes, mobyOffset + 0x58, 0x54);
    WriteInt32(mobyBytes, mobyOffset + 0x5c, 86);
    WriteInt32(mobyBytes, mobyOffset + 0x60, 77);
    WriteInt32(mobyBytes, mobyOffset + 0x64, 2);
    WriteInt32(mobyBytes, mobyOffset + 0x68, 2);
    WriteInt32(mobyBytes, mobyOffset + 0x6c, -1);

    var gameplay = DlGameplayBlockReader.ReadCore(BuildGameplayData(
        DlGameplayBlockReader.CoreHeaderSize,
        (0x30, mobyBytes)));
    var mobyInstances = gameplay.Blocks.Single(block => block.SemanticName == "moby_instances").MobyInstances;

    Expect(mobyInstances is not null, "core moby_instances block should be parsed into a typed model");
    Expect(mobyInstances!.StaticCount == 1, "moby instance static count should be parsed");
    Expect(mobyInstances.SpawnableMobyCount == 400, "moby instance spawnable count should be parsed");
    Expect(mobyInstances.TrailingBytes.Length == 0, "moby instance fixed records should consume the payload");

    var moby = mobyInstances.Instances.Single();
    Expect(moby.Size == DlMobyInstancesReader.RecordSize, "moby instance size field should be parsed");
    Expect(moby.Mission == -1, "moby instance mission should be parsed");
    Expect(moby.Uid == 0x78, "moby instance uid should be parsed");
    Expect(moby.Bolts == 4, "moby instance bolts should be parsed");
    Expect(moby.ClassId == 0x0b37, "moby instance class id should be parsed");
    Expect(moby.Scale == 1.5f, "moby instance scale should be parsed");
    Expect(moby.DrawDistance == 64, "moby instance draw distance should be parsed");
    Expect(moby.UpdateDistance == 80, "moby instance update distance should be parsed");
    Expect(moby.Unused20 == 32, "moby instance unused 0x20 sentinel should be parsed");
    Expect(moby.Unused24 == 64, "moby instance unused 0x24 sentinel should be parsed");
    Expect(moby.Position == new DlVector3(10, 20, 30), "moby instance position should be parsed");
    Expect(moby.Rotation == new DlVector3(0.25f, 0.5f, 0.75f), "moby instance rotation should be parsed");
    Expect(moby.Group == -1, "moby instance group should be parsed");
    Expect(moby.IsRooted == 1, "moby instance rooted flag should be parsed");
    Expect(moby.RootedDistance == -1, "moby instance rooted distance should be parsed");
    Expect(moby.Unused4C == 1, "moby instance unused 0x4c sentinel should be parsed");
    Expect(moby.PvarIndex == 10, "moby instance pvar index should be parsed");
    Expect(moby.Occlusion == 1, "moby instance occlusion should be parsed");
    Expect(moby.ModeBits == 0x54, "moby instance mode bits should be parsed");
    Expect(moby.Color == new DlRgb96(86, 77, 2), "moby instance color should be parsed");
    Expect(moby.Light == 2, "moby instance light should be parsed");
    Expect(moby.Unused6C == -1, "moby instance unused 0x6c sentinel should be parsed");

    var truncatedMobyBytes = new byte[0xf0];
    WriteInt32(truncatedMobyBytes, 0x00, 7);
    var truncatedGameplay = DlGameplayBlockReader.ReadCore(BuildGameplayData(
        DlGameplayBlockReader.CoreHeaderSize,
        (0x30, truncatedMobyBytes)));
    var truncatedMobyInstances = truncatedGameplay.Blocks
        .Single(block => block.SemanticName == "moby_instances")
        .MobyInstances;

    Expect(truncatedMobyInstances is null, "short moby instance payloads should not fail gameplay parsing");
}

static void ValidateCodeSegmentParsing()
{
    var data = new byte[0x10 + 4 + 0x10 + 2 + 3];
    WriteUInt32(data, 0x00, 0x12345678);
    WriteInt32(data, 0x04, 4);
    WriteInt32(data, 0x08, 2);
    WriteUInt32(data, 0x0c, 0x87654321);
    data[0x10] = 1;
    data[0x11] = 2;
    data[0x12] = 3;
    data[0x13] = 4;

    var secondOffset = 0x14;
    WriteUInt32(data, secondOffset, 0x11111111);
    WriteInt32(data, secondOffset + 0x04, 2);
    WriteInt32(data, secondOffset + 0x08, 7);
    WriteUInt32(data, secondOffset + 0x0c, 0x22222222);
    data[secondOffset + 0x10] = 0xaa;
    data[secondOffset + 0x11] = 0xbb;
    data[^3] = 0xfe;
    data[^2] = 0xed;
    data[^1] = 0xfa;

    var code = DlCodeSegmentReader.Read(data);
    Expect(code.Records.Count == 2, "DL code segment should parse complete patch records");
    Expect(code.Records[0].InjectAddress == 0x12345678, "DL code patch inject address should be parsed");
    Expect(code.Records[0].PayloadBytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "DL code patch payload bytes should be sliced");
    Expect(code.Records[1].Offset == secondOffset, "DL code patch offsets should be tracked");
    Expect(code.Records[1].EntrypointAddress == 0x22222222, "DL code patch entrypoint should be parsed");
    Expect(code.UnparsedTail.SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa }), "DL code segment should preserve incomplete trailing bytes");
}

static void ValidateHudBankParsing()
{
    var header = new byte[0xd8];
    WriteUInt16(header, 0x00, 2);
    WriteUInt16(header, 0x02, 1);
    WriteInt32(header, 0x04, 0xb4);
    WriteInt32(header, 0x08, 0xc4);
    WriteInt32(header, 0x0c, 0xc8);
    WriteInt32(header, 0x10, 0xd0);
    WriteInt32(header, 0x14, 0);
    WriteInt32(header, 0x18, 1);
    WriteInt32(header, 0x1c, 1);
    WriteInt32(header, 0x20, 1);
    WriteInt32(header, 0x24, 1);
    WriteInt32(header, 0x34, 1);
    WriteInt32(header, 0x38, 1);
    WriteInt32(header, 0x3c, 1);
    WriteInt32(header, 0x40, 1);
    WriteInt32(header, 0x44, 1);
    WriteInt32(header, 0x54, 0x10);
    WriteInt32(header, 0x58, 0x400);

    WriteUInt16(header, 0xb4, 0x1234);
    WriteUInt16(header, 0xb6, 1);
    WriteUInt16(header, 0xb8, 0);
    WriteUInt16(header, 0xbc, 0xffff);

    WriteInt16(header, 0xc4, 0);
    WriteInt16(header, 0xc6, 0);
    WriteUInt32(header, 0xc8, 0x80000000);
    WriteUInt32(header, 0xd0, 0x80000000);
    header[0xd6] = 2;
    header[0xd7] = 2;

    var bank0 = Enumerable.Range(0, 0x10).Select(value => (byte)value).ToArray();
    var bank1 = CreatePalette();

    var hud = HudBankReader.Read(header, [bank0, bank1]);
    Expect(hud.Header.IconCount == 2, "DL HUD icon count should be parsed");
    Expect(hud.Header.FrameCount == 1, "DL HUD frame count should be parsed");
    Expect(hud.Icons[0].IconId == 0x1234, "DL HUD icon id should be parsed");
    Expect(hud.Icons[0].FrameCount == 1 && hud.Icons[0].FirstFrameIndex == 0, "DL HUD icon frame range should be parsed");
    Expect(hud.Icons[1].IconId == 0xffff, "DL HUD icon terminator should be preserved");
    Expect(hud.Frames[0].PaletteIndex == 0 && hud.Frames[0].TextureIndex == 0, "DL HUD frame palette/texture handles should be parsed");
    Expect(HudBankReader.TryGetPalette(hud, 0, out var palette), "HUD palette should be addressable by id");
    Expect(HudBankReader.TryGetTexture(hud, 0, out var texture), "HUD texture should be addressable by id");
    Expect(palette.Offset == 0 && palette.BankIndex == 1, "DL HUD high-bit palette offset and bank should be decoded");
    Expect(texture.Offset == 0 && texture.BankIndex == 0, "DL HUD texture bank should be parsed from cumulative counts");
    Expect(texture.Width == 4 && texture.Height == 4, "DL HUD dimensions should be powers of two from u/v log metadata");
    Expect(texture.PixelBytes.SequenceEqual(bank0), "DL HUD texture bytes should be sliced from the source bank");

    var renderFiles = HudBankRenderPackageBuilder.BuildFiles(
        header,
        [CreateLiteralWad(bank0), bank1]);
    Expect(renderFiles.Any(file => file.Path == "hud/manifest.json"), "HUD render package should include its manifest");
    Expect(renderFiles.Any(file => file.Path == "hud/bank_0/tex.0000.png"), "HUD render package should include frame PNGs");

    var rc1Files = Rc1LevelWadRenderPackageBuilder.BuildFiles(
        1,
        [
            new PackedFile("assets/asset_header.bin", [1], "application/octet-stream"),
            new PackedFile("assets/palette.bin", [1], "application/octet-stream"),
            new PackedFile("assets/asset_wad.bin", [1], "application/octet-stream"),
            new PackedFile("hud/header.bin", header, "application/octet-stream"),
            new PackedFile("hud/bank0.bin", CreateLiteralWad(bank0), "application/octet-stream"),
            new PackedFile("hud/bank1.bin", bank1, "application/octet-stream")
        ],
        _ => []);
    Expect(rc1Files.Any(file => file.Path == "hud/manifest.json"), "RC1 render package should include the shared HUD manifest");
    Expect(rc1Files.Any(file => file.Path == "hud/bank_0/tex.0000.png"), "RC1 render package should include HUD frame PNGs");
}

static void ValidateWorldInstanceParsing()
{
    var directionalLights = new byte[0x10 + (2 * DlWorldInstanceReader.DirectionalLightRecordSize)];
    WriteInt32(directionalLights, 0, 2);
    WriteSingle(directionalLights, 0x10, 1.25f);
    WriteSingle(directionalLights, 0x20, 2.5f);

    var tieClassIds = new byte[0x10];
    WriteInt32(tieClassIds, 0, 2);
    WriteInt32(tieClassIds, 4, 0x2132);
    WriteInt32(tieClassIds, 8, 0x21e2);

    var tieInstances = new byte[0x10 + DlWorldInstanceReader.TieInstanceRecordSize];
    WriteInt32(tieInstances, 0, 1);

    var tieGroups = new byte[0x30];
    WriteInt32(tieGroups, 0, 1);
    WriteInt32(tieGroups, 4, 4);

    var shrubClassIds = new byte[0x08];
    WriteInt32(shrubClassIds, 0, 1);
    WriteInt32(shrubClassIds, 4, 0x20f0);

    var shrubInstances = new byte[0x10 + (2 * DlWorldInstanceReader.ShrubInstanceRecordSize)];
    WriteInt32(shrubInstances, 0, 2);

    var shrubGroups = new byte[0x30];
    WriteInt32(shrubGroups, 0, 1);
    WriteInt32(shrubGroups, 4, 2);

    var occlusionMapping = new byte[0x30];
    WriteInt32(occlusionMapping, 0, 1);
    WriteInt32(occlusionMapping, 4, 2);
    WriteInt32(occlusionMapping, 8, 3);

    var tieColors = new byte[]
    {
        0x02, 0x00, 0x02, 0x00, 0xaa, 0xbb, 0xcc, 0xdd,
        0xff, 0xff, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00
    };
    var worldBytes = BuildWorldInstanceData(
        (0x00, directionalLights),
        (0x04, tieClassIds),
        (0x08, tieInstances),
        (0x0c, tieGroups),
        (0x10, shrubClassIds),
        (0x14, shrubInstances),
        (0x18, shrubGroups),
        (0x1c, occlusionMapping),
        (0x20, tieColors));

    var world = DlWorldInstanceReader.Read(worldBytes);
    Expect(world.Length == worldBytes.Length, "world instance reader should preserve aggregate length");
    Expect(world.Slots.Count == 16, "world instance pointer table should contain 16 slots");
    Expect(world.Slots[0].SemanticName == "directional_lights", "slot 0x00 should be directional lights");
    var lighting = world.DirectionalLights ?? throw new InvalidOperationException("directional light table missing");
    var parsedTieClasses = world.TieClasses ?? throw new InvalidOperationException("tie class id list missing");
    var parsedTieInstances = world.TieInstances ?? throw new InvalidOperationException("tie instance table missing");
    var parsedTieGroups = world.TieGroups ?? throw new InvalidOperationException("tie group table missing");
    var parsedShrubClasses = world.ShrubClasses ?? throw new InvalidOperationException("shrub class id list missing");
    var parsedShrubInstances = world.ShrubInstances ?? throw new InvalidOperationException("shrub instance table missing");
    var parsedOcclusionMapping = world.OcclusionMapping ?? throw new InvalidOperationException("occlusion mapping table missing");
    var parsedTieColors = world.TieInstanceColors ?? throw new InvalidOperationException("tie instance colors missing");

    Expect(lighting.Count == 2, "directional light count should be parsed");
    Expect(lighting.RecordSize == 0x40, "directional light records should be 0x40 bytes");
    Expect(Math.Abs(lighting.Records[0].Vectors[0][0] - 1.25f) < 0.001f, "directional light vector floats should be parsed");
    Expect(parsedTieClasses.ClassIds.SequenceEqual([0x2132, 0x21e2]), "tie class ids should be parsed");
    Expect(parsedTieClasses.PaddingLength == 4, "tie class id padding should be tracked");
    Expect(parsedTieInstances.Count == 1, "tie instance count should be parsed");
    Expect(parsedTieInstances.RecordSize == 0x60, "tie instance records should be 0x60 bytes");
    Expect(parsedTieGroups.GroupCount == 1, "tie group count should be parsed");
    Expect(parsedTieGroups.GroupDataStartOffset == 0x20, "tie group data should start after aligned group offsets");
    Expect(parsedShrubClasses.ClassIds.SequenceEqual([0x20f0]), "shrub class ids should be parsed");
    Expect(parsedShrubInstances.Count == 2, "shrub instance count should be parsed");
    Expect(parsedShrubInstances.RecordSize == 0x70, "shrub instance records should be 0x70 bytes");
    Expect(parsedOcclusionMapping.TfragCount == 1, "occlusion tfrag mapping count should be parsed");
    Expect(parsedOcclusionMapping.TieCount == 2, "occlusion tie mapping count should be parsed");
    Expect(parsedOcclusionMapping.MobyCount == 3, "occlusion moby mapping count should be parsed");
    Expect(parsedTieColors.Length == tieColors.Length, "tie instance color payload length should be preserved");
    Expect(parsedTieColors.IsLengthValid, "tie instance color entries should consume the full payload");
    Expect(parsedTieColors.EntryCount == 3, "tie instance color entry count should be parsed");
    Expect(parsedTieColors.MappedInstanceCount == 1, "tie instance color ids should be mapped once");
    Expect(parsedTieColors.SentinelCount == 1, "tie instance color sentinel entries should be counted");
    Expect(parsedTieColors.DuplicateIdCount == 1, "tie instance color duplicate ids should be counted");
    Expect(parsedTieColors.MinInstanceId == 2 && parsedTieColors.MaxInstanceId == 2, "tie instance color id range should be tracked");

    var invalidPointer = new byte[DlWorldInstanceReader.PointerTableLength];
    WriteInt32(invalidPointer, 0, invalidPointer.Length + 1);
    ExpectThrows<InvalidDataException>(() => DlWorldInstanceReader.Read(invalidPointer));
}

static void ValidateAssetSlicing()
{
    var assetData = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    var knownOffsets = new[] { 0, 10, 20, assetData.Length };

    var defaultZeroOffsetSlice = DlAssetReader.ReadAssetSlice(assetData, 0, knownOffsets);
    Expect(defaultZeroOffsetSlice.Length == 0, "asset offset zero should be treated as absent by default");

    var tfragSlice = DlAssetReader.ReadAssetSlice(assetData, 0, knownOffsets, allowZeroOffset: true);
    Expect(tfragSlice.SequenceEqual(assetData[..10]), "tfrag asset slices should allow offset zero and stop at the next known asset offset");

    var nonZeroSlice = DlAssetReader.ReadAssetSlice(assetData, 10, knownOffsets);
    Expect(nonZeroSlice.SequenceEqual(assetData[10..20]), "non-zero asset slices should stop at the next known asset offset");

    var headerBytes = new byte[0xc0];
    WriteInt32(headerBytes, 0x10, 0x100);
    WriteInt32(headerBytes, 0x14, 0x1000);
    WriteInt32(headerBytes, 0x78, 0x200);
    var header = DlAssetReader.ReadHeader(headerBytes);
    var gcOffsets = DlAssetReader.CollectKnownAssetOffsets(GameId.GC, header, 0x2000, [], [], []);
    var dlOffsets = DlAssetReader.CollectKnownAssetOffsets(GameId.DL, header, 0x2000, [], [], []);
    Expect(!gcOffsets.Contains(0x200), "GC ratchet sequence table pointers should not truncate asset slices");
    Expect(dlOffsets.Contains(0x200), "DL light cuboid offsets should remain asset slice boundaries");
}

static void ValidateMobyGsStashTextures()
{
    const int classId = 0x251c;
    var headerBytes = new byte[0x100];
    WriteInt32(headerBytes, 0x18, 1);
    WriteInt32(headerBytes, 0x1c, 0xc0);
    WriteInt32(headerBytes, 0x38, 1);
    WriteInt32(headerBytes, 0x3c, 0xe0);
    WriteInt32(headerBytes, 0xac, 0xf0);
    WriteInt32(headerBytes, 0xc0, 0x200);
    WriteInt32(headerBytes, 0xc4, classId);
    headerBytes.AsSpan(0xd0, 0x10).Fill(0xff);
    headerBytes[0xd0] = 0;
    WriteInt32(headerBytes, 0xe0, 0x100);
    WriteInt16(headerBytes, 0xe4, 16);
    WriteInt16(headerBytes, 0xe6, 16);
    WriteInt16(headerBytes, 0xe8, 1);
    WriteInt16(headerBytes, 0xea, 0);
    WriteInt16(headerBytes, 0xec, -1);
    WriteInt16(headerBytes, 0xee, -1);
    WriteInt16(headerBytes, 0xf0, classId);
    WriteInt16(headerBytes, 0xf2, -1);

    var classIds = DlAssetReader.ReadMobyGsStashClassIds(headerBytes, 0xf0);
    Expect(classIds.SequenceEqual([classId]), "moby GS stash class ids should be read through the -1 terminator");

    var palette = CreatePalette();
    var assetBytes = new byte[0x201];
    for (var i = 0; i < 0x100; i++)
    {
        assetBytes[0x100 + i] = (byte)i;
    }
    assetBytes[0x200] = 1;

    var files = DlLevelWadRenderPackageBuilder.BuildAssetFiles(
        GameId.DL,
        levelIndex: 1,
        headerBytes,
        palette,
        assetBytes);
    var exportedPng = files.Single(file =>
        file.Path == "assets/moby/09500_251C/textures/tex.0000.png").Bytes;
    var definition = DlAssetReader.ReadTextureDefinitions(headerBytes, 0xe0, 1).Single();
    var unswizzledPng = DlAssetReader.BuildAssetTexture(
        "moby",
        0,
        definition,
        palette,
        assetBytes,
        textureDataOffset: 0,
        isSwizzled: false).PngBytes;
    var swizzledPng = DlAssetReader.BuildAssetTexture(
        "moby",
        0,
        definition,
        palette,
        assetBytes,
        textureDataOffset: 0,
        isSwizzled: true).PngBytes;

    Expect(exportedPng.SequenceEqual(unswizzledPng), "GS-stashed moby textures should be exported without swizzle");
    Expect(!exportedPng.SequenceEqual(swizzledPng), "GS-stashed moby textures should not use the normal DL swizzle");
}

static void ValidateEnvironmentTextureRenderPackage()
{
    var headerBytes = new byte[0xe0];
    WriteInt32(headerBytes, 0x04, 0xc0);
    WriteInt32(headerBytes, 0x84, 2);
    WriteInt32(headerBytes, 0x90, 0);
    WriteInt32(headerBytes, 0x94, 0);
    WriteInt32(headerBytes, 0x98, 0x222);
    WriteInt32(headerBytes, 0x9c, 0x400);

    WriteInt16(headerBytes, 0xc4, 2);
    WriteInt16(headerBytes, 0xc6, 2);
    WriteInt32(headerBytes, 0xc8, 0x800);
    WriteInt32(headerBytes, 0xcc, 0);
    WriteInt16(headerBytes, 0xd4, 2);
    WriteInt16(headerBytes, 0xd6, 2);
    WriteInt32(headerBytes, 0xd8, 0x804);
    WriteInt32(headerBytes, 0xdc, 0x222);

    var paletteBytes = new byte[0x808];
    CreatePalette().CopyTo(paletteBytes, 0);
    CreatePalette().CopyTo(paletteBytes, 0x400);
    new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }.CopyTo(paletteBytes, 0x800);

    var files = DlLevelWadRenderPackageBuilder.BuildAssetFiles(
        GameId.DL,
        levelIndex: 1,
        headerBytes,
        paletteBytes,
        assetBytes: []);
    var byPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
    Expect(byPath.ContainsKey("assets/environment/chrome.png"), "render package should export the level chrome texture");
    Expect(byPath.ContainsKey("assets/environment/glass.png"), "render package should export the level glass texture");

    using var manifest = JsonDocument.Parse(byPath["assets/manifest.json"].Bytes);
    var textures = manifest.RootElement.GetProperty("EnvironmentTextures");
    Expect(textures.GetProperty("chrome").GetString() == "environment/chrome.png", "asset manifest should locate chrome");
    Expect(textures.GetProperty("glass").GetString() == "environment/glass.png", "asset manifest should locate glass");
}

static void ValidateDzoGlbExportWhenAvailable()
{
    var fixtureRoot = Path.Combine("test-assets", "DL Mobys", "09500_251C");
    var modelPath = Path.Combine(fixtureRoot, "moby.bin");
    var texturePath = Path.Combine(fixtureRoot, "tex.0000.0.png");
    if (!File.Exists(modelPath) || !File.Exists(texturePath))
    {
        return;
    }

    var textureBytes = File.ReadAllBytes(texturePath);
    var glb = DlDzoMobyExporter.ExportMoby(
        File.ReadAllBytes(modelPath),
        [textureBytes]);
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb) == 0x46546c67, "DZO export should write the GLB magic");
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)) == 2, "DZO export should write GLB version 2");
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)) == glb.Length, "DZO GLB length should match its header");

    var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12)));
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16)) == 0x4e4f534a, "DZO GLB should start with a JSON chunk");
    using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
    var root = json.RootElement;
    Expect(!root.GetProperty("buffers")[0].TryGetProperty("uri", out _), "DZO GLB buffer should be embedded");
    Expect(!root.TryGetProperty("animations", out _), "DZO GLB should not contain baked animations");
    var image = root.GetProperty("images")[0];
    Expect(!image.TryGetProperty("uri", out _), "DZO GLB image should be embedded");
    Expect(image.GetProperty("mimeType").GetString() == "image/png", "DZO GLB image should retain its PNG media type");

    var binHeaderOffset = checked(20 + jsonLength);
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHeaderOffset + 4)) == 0x004e4942, "DZO GLB should contain a BIN chunk");
    var imageView = root.GetProperty("bufferViews")[image.GetProperty("bufferView").GetInt32()];
    var imageOffset = checked(binHeaderOffset + 8 + imageView.GetProperty("byteOffset").GetInt32());
    Expect(
        glb.AsSpan(imageOffset, 8).SequenceEqual(textureBytes.AsSpan(0, 8)),
        "DZO GLB should contain the source PNG bytes in its BIN chunk");

    var meshMaterialKeys = new HashSet<int>();
    foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
    {
        Expect(mesh.GetProperty("primitives").GetArrayLength() == 1,
            "DZO should fully combine each material into one mesh primitive");
        int? meshMaterial = null;
        foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            var material = primitive.TryGetProperty("material", out var materialElement)
                ? materialElement.GetInt32()
                : -1;
            meshMaterial ??= material;
            Expect(meshMaterial == material, "DZO should merge mesh primitives by material");
            var attributes = primitive.GetProperty("attributes");
            Expect(!attributes.TryGetProperty("COLOR_0", out _),
                "DZO bangle IDs should not use COLOR_0 because glTF multiplies it into base color");
            Expect(attributes.TryGetProperty("TEXCOORD_1", out var bangleUvAccessorElement),
                "DZO mesh primitives should include bangle IDs in the second UV map");
            var bangleUvAccessor = root.GetProperty("accessors")[bangleUvAccessorElement.GetInt32()];
            Expect(bangleUvAccessor.GetProperty("type").GetString() == "VEC2",
                "DZO bangle UV accessor should be VEC2 for glTF and Unity compatibility");
            var minEmissionWeight = bangleUvAccessor.GetProperty("min")[1].GetSingle();
            var maxEmissionWeight = bangleUvAccessor.GetProperty("max")[1].GetSingle();
            Expect(minEmissionWeight is 0f or 1f && maxEmissionWeight is 0f or 1f,
                "DZO UV2.y should contain binary inverse-emission weights");
            var decodedBangleIndex = bangleUvAccessor.GetProperty("min")[0].GetSingle() * 255f;
            Expect(
                decodedBangleIndex >= 0f
                && decodedBangleIndex <= 15f
                && MathF.Abs(decodedBangleIndex - MathF.Round(decodedBangleIndex)) < 0.0001f,
                "DZO bangle ID attributes should normalize byte-sized indices by 255");
        }
        Expect(meshMaterial.HasValue && meshMaterialKeys.Add(meshMaterial.Value),
            "DZO should emit only one mesh for each material");
    }
}

static void ValidateDzoMobyExportConventions()
{
    var commonTransforms = new byte[0x20];
    WriteSingle(commonTransforms, 0x00, 5120f);
    commonTransforms[0x0c] = 0x7f;
    WriteSingle(commonTransforms, 0x10, 2048f);
    commonTransforms[0x1c] = 0;

    var model = new MobyModel
    {
        AnimationFormat = MobyAnimationFormat.Compact,
        SkeletonFormat = MobyAnimationFormat.Compact,
        JointCount = 2,
        Scale = 2048f,
        CommonTransforms = commonTransforms,
        Skeleton = new MobySkeleton(),
        MeshTable = new MobyMeshTable()
    };
    model.Skeleton.Bones.Add(CreateIdentityMobyBone(5120f));
    model.Skeleton.Bones.Add(CreateIdentityMobyBone(7168f));

    var export = DlDzoMobyExporter.ExportGltf(
        model,
        options: new MobyDzoGltfExportOptions
        {
            FlattenJointHierarchy = true
        });

    using var json = JsonDocument.Parse(export.GltfBytes);
    var root = json.RootElement;
    Expect(!root.TryGetProperty("animations", out _), "DZO moby export should omit baked animations");
    var skinJoints = root.GetProperty("skins")[0].GetProperty("joints").EnumerateArray().Select(value => value.GetInt32()).ToArray();
    var nodes = root.GetProperty("nodes");
    Expect(nodes[skinJoints[0]].GetProperty("name").GetString() == "joint_0", "DZO root joint should use joint_0 naming");
    Expect(nodes[skinJoints[1]].GetProperty("name").GetString() == "joint_1", "DZO child joint should use joint_1 naming");
    Expect(MathF.Abs(nodes[skinJoints[0]].GetProperty("translation")[0].GetSingle() - 5f) < 0.0001f,
        "DZO root joint should not bake the moby header scale into its translation");
    Expect(!nodes[skinJoints[0]].TryGetProperty("children", out _), "DZO joints should have a flattened hierarchy");
    Expect(!nodes[skinJoints[1]].TryGetProperty("children", out _), "DZO joints should not parent other joints");
    Expect(MathF.Abs(nodes[skinJoints[1]].GetProperty("translation")[0].GetSingle() - 7f) < 0.0001f,
        "DZO flattened child joint should retain its world-space translation");

    var armature = nodes.EnumerateArray().Single(node => node.GetProperty("name").GetString() == "Armature");
    Expect(
        armature.GetProperty("children").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(skinJoints),
        "DZO armature should directly contain every joint");

    var treeExport = DlDzoMobyExporter.ExportGltf(
        model,
        options: new MobyDzoGltfExportOptions
        {
            FlattenJointHierarchy = false
        });
    using var treeJson = JsonDocument.Parse(treeExport.GltfBytes);
    var treeNodes = treeJson.RootElement.GetProperty("nodes");
    var treeSkinJoints = treeJson.RootElement.GetProperty("skins")[0].GetProperty("joints")
        .EnumerateArray()
        .Select(value => value.GetInt32())
        .ToArray();
    Expect(
        treeNodes[treeSkinJoints[0]].GetProperty("children").EnumerateArray()
            .Select(value => value.GetInt32())
            .SequenceEqual([treeSkinJoints[1]]),
        "DZO tree hierarchy should parent child joints under their decoded parents");
    Expect(MathF.Abs(treeNodes[treeSkinJoints[1]].GetProperty("translation")[0].GetSingle() - 2f) < 0.0001f,
        "DZO tree hierarchy should use parent-relative joint translations");
    var treeArmature = treeNodes.EnumerateArray().Single(node => node.GetProperty("name").GetString() == "Armature");
    Expect(
        treeArmature.GetProperty("children").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual([treeSkinJoints[0]]),
        "DZO tree hierarchy should attach only root joints directly to the armature");
}

static MobyMatrix4 CreateIdentityMobyBone(float x)
{
    return new MobyMatrix4
    {
        Row1 = new MobyMatrixRow { X = 1f, W = x },
        Row2 = new MobyMatrixRow { Y = 1f },
        Row3 = new MobyMatrixRow { Z = 1f },
        Row4 = new MobyMatrixRow { W = 1f }
    };
}

static void ValidateDzoMetalAndGlowExport()
{
    var textureIds = Enumerable.Repeat((byte)0xff, 12).ToArray();
    textureIds[0] = 0;
    var model = new MobyModel
    {
        AnimationFormat = MobyAnimationFormat.Compact,
        SkeletonFormat = MobyAnimationFormat.Compact,
        HighLodMeshCount = 2,
        MetalCount = 1,
        MetalOffsets = 2,
        Scale = 1024f,
        GlowRgba = unchecked((int)0x80402010),
        MeshTable = new MobyMeshTable()
    };
    model.MeshTable.Entries.Add(CreateDzoTestMesh(MobyMeshType.HighLod, includeTexCoords: true, textureIds));
    model.MeshTable.Entries.Add(CreateDzoTestMesh(MobyMeshType.HighLod, includeTexCoords: true, textureIds));
    model.MeshTable.Entries.Add(CreateDzoTestMesh(MobyMeshType.Metal, includeTexCoords: false, textureIds));

    var options = new MobyDzoGltfExportOptions
    {
        ExternalTextureUris = new Dictionary<int, string> { [0] = "tex.0000.png" },
        ExternalTextureSizes = new Dictionary<int, TextureSize> { [0] = new TextureSize(8, 8) },
        ExternalTextureAlpha = new Dictionary<int, TextureAlphaInfo> { [0] = TextureAlphaInfo.Opaque }
    };
    var export = DlDzoMobyExporter.ExportGltf(model, options: options);
    using var json = JsonDocument.Parse(export.GltfBytes);
    var root = json.RootElement;
    Expect(root.GetProperty("meshes").EnumerateArray()
            .All(mesh => mesh.GetProperty("primitives").GetArrayLength() == 1),
        "DZO moby export should emit exactly one primitive per material");
    Expect(root.GetProperty("meshes").EnumerateArray()
            .SelectMany(mesh => mesh.GetProperty("primitives").EnumerateArray())
            .Any(primitive => primitive.GetProperty("attributes").TryGetProperty("_MOBY_METAL_REFLECTION_SCALE", out _)),
        "DZO moby export should include metal mesh primitives");
    var metalPrimitive = root.GetProperty("meshes").EnumerateArray()
        .SelectMany(mesh => mesh.GetProperty("primitives").EnumerateArray())
        .Single(primitive => primitive.GetProperty("attributes").TryGetProperty("_MOBY_METAL_REFLECTION_SCALE", out _));
    Expect(metalPrimitive.TryGetProperty("material", out var metalMaterialIndex),
        "DZO metal overlays should reference an explicit material");
    var metalMaterial = root.GetProperty("materials")[metalMaterialIndex.GetInt32()];
    Expect(metalMaterial.GetProperty("name").GetString() == "metal",
        "DZO metal overlays should retain the metal material identity");
    Expect(metalMaterial.GetProperty("extras").GetProperty("MobyMaterialKind").GetString() == "Metal",
        "DZO metal overlays should identify their material kind");
    var metalPbr = metalMaterial.GetProperty("pbrMetallicRoughness");
    Expect(metalPbr.GetProperty("metallicFactor").GetSingle() == 1f
        && metalPbr.GetProperty("roughnessFactor").GetSingle() == 0f,
        "DZO metal overlays should export a valid reflective PBR material");
    var metalEmissiveFactor = metalMaterial.GetProperty("emissiveFactor");
    Expect(metalEmissiveFactor.EnumerateArray().All(component => component.GetSingle() == 0f)
        && !metalMaterial.TryGetProperty("emissiveTexture", out _),
        "DZO metal overlays should not inherit moby glow emission");
    var nonMetalMaterialIndices = root.GetProperty("meshes").EnumerateArray()
        .SelectMany(mesh => mesh.GetProperty("primitives").EnumerateArray())
        .Where(primitive => !primitive.GetProperty("attributes").TryGetProperty("_MOBY_METAL_REFLECTION_SCALE", out _))
        .Select(primitive => primitive.GetProperty("material").GetInt32())
        .ToArray();
    Expect(nonMetalMaterialIndices.All(index => index != metalMaterialIndex.GetInt32()),
        "DZO metal overlays should not be merged with ordinary geometry");
    var glowPrimitive = root.GetProperty("meshes").EnumerateArray()
        .SelectMany(mesh => mesh.GetProperty("primitives").EnumerateArray())
        .Single(primitive => primitive.GetProperty("extras").GetProperty("MobyGlowVertexCount").GetInt32() > 0);
    var glowUvAccessorIndex = glowPrimitive.GetProperty("attributes").GetProperty("TEXCOORD_1").GetInt32();
    var glowUvAccessor = root.GetProperty("accessors")[glowUvAccessorIndex];
    Expect(glowUvAccessor.GetProperty("min")[1].GetSingle() == 0f
        && glowUvAccessor.GetProperty("max")[1].GetSingle() == 0f,
        "DZO glow packets should store zero in inverse-emission UV2.y");
    var glowMaterial = root.GetProperty("materials")[glowPrimitive.GetProperty("material").GetInt32()];
    var emissiveFactor = glowMaterial.GetProperty("emissiveFactor");
    Expect(emissiveFactor.EnumerateArray().All(component => component.GetSingle() == 0f),
        "DZO materials should default their emissive factor to zero");
    Expect(glowMaterial.GetProperty("emissiveTexture").GetProperty("index").GetInt32()
        == glowMaterial.GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture").GetProperty("index").GetInt32(),
        "DZO textured materials should reuse their base texture for emission");
    var metalUvAccessorIndex = metalPrimitive.GetProperty("attributes").GetProperty("TEXCOORD_1").GetInt32();
    var metalUvAccessor = root.GetProperty("accessors")[metalUvAccessorIndex];
    Expect(metalUvAccessor.GetProperty("min")[1].GetSingle() == 0.5f
        && metalUvAccessor.GetProperty("max")[1].GetSingle() == 0.5f,
        "DZO metal packets should store one minus reflection strength in UV2.y for Unity's Y flip");
    var metalTextureUvAccessorIndex = metalPrimitive.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32();
    var metalTextureUvAccessor = root.GetProperty("accessors")[metalTextureUvAccessorIndex];
    Expect(metalTextureUvAccessor.GetProperty("min")[0].GetSingle() == 0f
        && metalTextureUvAccessor.GetProperty("min")[1].GetSingle() == 0f
        && metalTextureUvAccessor.GetProperty("max")[0].GetSingle() == 0f
        && metalTextureUvAccessor.GetProperty("max")[1].GetSingle() == 0f,
        "DZO metal meshes should include a dummy UV1 so importers retain bangle metadata in UV2");
    Expect(root.GetProperty("materials").EnumerateArray()
            .Where(material => material.GetProperty("name").GetString() != "metal")
            .All(material => material.TryGetProperty("emissiveTexture", out _)),
        "DZO textured materials should expose their base textures as emission textures");

    model.GlowRgba = 0;
    var noGlowExport = DlDzoMobyExporter.ExportGltf(model, options: options);
    using var noGlowJson = JsonDocument.Parse(noGlowExport.GltfBytes);
    var noGlowMaterial = noGlowJson.RootElement.GetProperty("materials")[0];
    Expect(noGlowMaterial.TryGetProperty("emissiveTexture", out _)
        && noGlowMaterial.GetProperty("emissiveFactor").EnumerateArray()
            .All(component => component.GetSingle() == 0f),
        "DZO textured materials should retain their emission texture with emission disabled by default");
}

static void ValidateDzoTeamTextureVariants()
{
    var textureIds = Enumerable.Repeat((byte)0xff, 12).ToArray();
    textureIds[0] = 0;
    var model = new MobyModel
    {
        AnimationFormat = MobyAnimationFormat.Compact,
        SkeletonFormat = MobyAnimationFormat.Compact,
        HighLodMeshCount = 1,
        Scale = 1024f,
        TeamPalettes = 0x1b,
        MeshTable = new MobyMeshTable()
    };
    model.MeshTable.Entries.Add(CreateDzoTestMesh(MobyMeshType.HighLod, includeTexCoords: true, textureIds));

    var teamPalettes = new List<byte[]>();
    for (var teamId = 0; teamId < 11; teamId++)
    {
        var palette = CreatePalette();
        palette[0] = (byte)(teamId + 1);
        teamPalettes.Add(palette);
    }
    model.TeamPaletteData.Add(0, teamPalettes);

    var sourceTexture = PifWriter.CreateIndexed8(
        8,
        8,
        CreatePalette(),
        new byte[64]);
    var glb = DlDzoMobyExporter.ExportMoby(
        model,
        new[] { sourceTexture });

    var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12)));
    using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
    var root = json.RootElement;
    Expect(root.GetProperty("extensionsUsed").EnumerateArray()
            .Any(extension => extension.GetString() == "KHR_materials_variants"),
        "DZO team textures should declare KHR_materials_variants");
    var expectedTeamNames = new[]
    {
        "Blue", "Red", "Green", "Orange", "Yellow", "Purple",
        "Aqua", "Pink", "Olive", "Maroon", "White"
    };
    var variants = root.GetProperty("extensions")
        .GetProperty("KHR_materials_variants")
        .GetProperty("variants");
    Expect(
        variants.EnumerateArray().Select(variant => variant.GetProperty("name").GetString())
            .SequenceEqual(expectedTeamNames),
        "DZO team material presets should use the requested team names");
    Expect(root.GetProperty("images").GetArrayLength() == 12,
        "DZO team material presets should embed the base texture and all 11 team textures");
    Expect(root.GetProperty("materials").GetArrayLength() == 12,
        "DZO team material presets should retain the base material and add 11 team materials");

    var primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
    var mappings = primitive.GetProperty("extensions")
        .GetProperty("KHR_materials_variants")
        .GetProperty("mappings");
    Expect(mappings.GetArrayLength() == 11,
        "DZO textured primitives should map every team preset to a team material");
    var baseMaterial = root.GetProperty("materials")[primitive.GetProperty("material").GetInt32()];
    var baseTextureIndex = baseMaterial.GetProperty("pbrMetallicRoughness")
        .GetProperty("baseColorTexture")
        .GetProperty("index")
        .GetInt32();
    foreach (var (mapping, teamIndex) in mappings.EnumerateArray().Select((mapping, index) => (mapping, index)))
    {
        Expect(mapping.GetProperty("variants")[0].GetInt32() == teamIndex,
            "DZO team mappings should retain their numeric team order");
        var material = root.GetProperty("materials")[mapping.GetProperty("material").GetInt32()];
        Expect(material.GetProperty("name").GetString() == expectedTeamNames[teamIndex],
            "DZO team materials should use their team names");
        Expect(material.GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture")
                .GetProperty("index").GetInt32() != baseTextureIndex,
            "DZO team material presets should replace the base color texture");
        Expect(material.GetProperty("emissiveTexture").GetProperty("index").GetInt32()
                == material.GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture")
                    .GetProperty("index").GetInt32(),
            "DZO team material presets should reuse their selected base texture for emission");
        Expect(material.GetProperty("pbrMetallicRoughness").GetProperty("metallicFactor").GetSingle()
                == baseMaterial.GetProperty("pbrMetallicRoughness").GetProperty("metallicFactor").GetSingle(),
            "DZO team material presets should preserve non-base-texture material settings");
    }
}

static void ValidateDzoTextureAlphaModes()
{
    var opaquePalette = CreatePaletteWithAlpha(128);
    var maskPalette = CreatePaletteWithAlpha(128);
    maskPalette[3] = 0;
    var blendPalette = CreatePaletteWithAlpha(128);
    blendPalette[3] = 0;
    blendPalette[7] = 64;
    var maskTexture = PifWriter.CreateIndexed8(
        8,
        8,
        maskPalette,
        Enumerable.Range(0, 64).Select(index => (byte)(index % 2)).ToArray());
    using var opaqueJson = ExportTextureMaterial(PifWriter.CreateIndexed8(8, 8, opaquePalette, new byte[64]));
    using var maskJson = ExportTextureMaterial(maskTexture);
    using var strictMaskJson = ExportTextureMaterial(maskTexture, nonOpaqueAlphaCoverageThreshold: 0.5f);
    using var blendJson = ExportTextureMaterial(PifWriter.CreateIndexed8(
        8,
        8,
        blendPalette,
        Enumerable.Range(0, 64).Select(index => (byte)(index % 3)).ToArray()));
    var opaqueMaterial = opaqueJson.RootElement.GetProperty("materials")[0];
    var maskMaterial = maskJson.RootElement.GetProperty("materials")[0];
    var blendMaterial = blendJson.RootElement.GetProperty("materials")[0];
    Expect(!opaqueMaterial.TryGetProperty("alphaMode", out _),
        "DZO textures containing only PS2 alpha 128 should export as opaque");
    Expect(opaqueMaterial.GetProperty("extras").GetProperty("MinAlpha").GetInt32() == 255,
        "DZO opaque PS2 alpha should normalize from 128 to 255");
    Expect(maskMaterial.GetProperty("alphaMode").GetString() == "MASK",
        "DZO textures containing only PS2 alpha 0 and 128 should export as masked");
    Expect(!strictMaskJson.RootElement.GetProperty("materials")[0].TryGetProperty("alphaMode", out _),
        "DZO texture alpha coverage threshold should remain configurable");
    Expect(blendMaterial.GetProperty("alphaMode").GetString() == "BLEND",
        "DZO textures containing intermediate PS2 alpha should export as blended");

    static JsonDocument ExportTextureMaterial(
        PifTextureData texture,
        float nonOpaqueAlphaCoverageThreshold = MobyDzoGltfExportOptions.DefaultNonOpaqueAlphaCoverageThreshold)
    {
        var textureIds = Enumerable.Repeat((byte)0xff, 12).ToArray();
        textureIds[0] = 0;
        var model = new MobyModel
        {
            AnimationFormat = MobyAnimationFormat.Compact,
            SkeletonFormat = MobyAnimationFormat.Compact,
            HighLodMeshCount = 1,
            Scale = 1024f,
            MeshTable = new MobyMeshTable()
        };
        model.MeshTable.Entries.Add(CreateDzoTestMesh(MobyMeshType.HighLod, includeTexCoords: true, textureIds));
        var glb = DlDzoMobyExporter.ExportMoby(
            model,
            new[] { texture },
            nonOpaqueAlphaCoverageThreshold: nonOpaqueAlphaCoverageThreshold);
        var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12)));
        return JsonDocument.Parse(glb.AsMemory(20, jsonLength));
    }
}

static byte[] CreatePaletteWithAlpha(byte alpha)
{
    var palette = CreatePalette();
    for (var offset = 3; offset < palette.Length; offset += 4)
    {
        palette[offset] = alpha;
    }

    return palette;
}

static MobyMeshTableEntry CreateDzoTestMesh(MobyMeshType meshType, bool includeTexCoords, byte[] textureIds)
{
    var vertexData = meshType == MobyMeshType.Metal ? new byte[0x40] : new byte[0x40];
    BinaryPrimitives.WriteUInt16LittleEndian(vertexData, 3);
    if (meshType != MobyMeshType.Metal)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(vertexData.AsSpan(0x06), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(vertexData.AsSpan(0x0a), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(vertexData.AsSpan(0x0c), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(vertexData.AsSpan(0x0e), 3);
    }
    var vertexBase = 0x10;
    WriteMobyTestPosition(vertexData, vertexBase, 0, 0, 0, meshType);
    WriteMobyTestPosition(vertexData, vertexBase + 0x10, 1, 0, 0, meshType);
    WriteMobyTestPosition(vertexData, vertexBase + 0x20, 0, 1, 0, meshType);

    var vifData = new List<byte>();
    if (includeTexCoords)
    {
        vifData.AddRange([0, 0, 3, 0x65]);
        foreach (var (s, t) in new[] { (512, 512), (2048, 512), (512, 2048) })
        {
            vifData.AddRange(BitConverter.GetBytes((short)s));
            vifData.AddRange(BitConverter.GetBytes((short)t));
        }
    }
    vifData.AddRange([0, 0, 2, 0x6e, 0, 0, 0, 0, 1, 2, 3, 3]);

    return new MobyMeshTableEntry
    {
        MeshType = meshType,
        VertexCount = 3,
        VertexData = vertexData,
        VifData = vifData.ToArray(),
        GifTag = new MobyGifTag { TextureIds = (byte[])textureIds.Clone() }
    };
}

static void WriteMobyTestPosition(byte[] data, int offset, short x, short y, short z, MobyMeshType meshType)
{
    var positionOffset = meshType == MobyMeshType.Metal ? offset : offset + 0x0a;
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(positionOffset), x);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(positionOffset + 2), y);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(positionOffset + 4), z);
}

static void ValidatePifMipRoundtrip()
{
    var palette = CreatePalette();
    var basePixels = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
    var mip1 = new byte[] { 1, 2, 3, 4 };
    var mip2 = new byte[] { 5 };

    var texture = PifWriter.CreateIndexed8(
        4,
        4,
        palette,
        basePixels,
        [mip1, mip2],
        isSwizzled: true);
    var pifBytes = PifWriter.Write(texture);
    var roundtrip = PifReader.Read(pifBytes);

    Expect(roundtrip.Header.FileSize == pifBytes.Length, "PIF header file size should match serialized size");
    Expect(roundtrip.Header.USize == 4 && roundtrip.Header.VSize == 4, "PIF dimensions should roundtrip");
    Expect(roundtrip.Header.MipLevels == 3, "PIF mip level count should include base mip");
    Expect(roundtrip.IsSwizzled, "PIF swizzle flag should roundtrip");
    Expect(roundtrip.PaletteData.SequenceEqual(palette), "PIF palette bytes should roundtrip");
    Expect(roundtrip.PixelData.SequenceEqual(basePixels), "PIF base pixel bytes should roundtrip");
    Expect(roundtrip.MipPixelData.Count == 2, "PIF should retain two mip payloads");
    Expect(roundtrip.MipPixelData[0].SequenceEqual(mip1), "PIF mip 1 bytes should roundtrip");
    Expect(roundtrip.MipPixelData[1].SequenceEqual(mip2), "PIF mip 2 bytes should roundtrip");

    var pngBytes = RatchetPs2.Core.Textures.TextureConverter.ConvertToPng(roundtrip);
    using var pngStream = new MemoryStream(pngBytes, writable: false);
    var metadata = PngTextureMetadataReader.ReadPng(pngStream);
    Expect(metadata.Size.Width == 4 && metadata.Size.Height == 4, "PNG preview should use base mip dimensions");

    var halfPaletteTexture = PifWriter.CreateIndexed8(
        2,
        2,
        palette[..0x200],
        [0, 1, 2, 3]);
    var halfPaletteRoundtrip = PifReader.Read(PifWriter.Write(halfPaletteTexture));
    Expect(halfPaletteRoundtrip.Header.PaletteFormat != 0, "0x200-byte PIF palettes should use a non-zero palette format");
    Expect(halfPaletteRoundtrip.PaletteData.Length == 0x200, "0x200-byte PIF palettes should roundtrip with the expected size");
    ExpectThrows<ArgumentException>(() => PifWriter.CreateIndexed8(
        2,
        2,
        palette,
        [0, 1, 2, 3],
        paletteFormat: 1));
}

static void ValidateNormalizedTextureArtifacts()
{
    var palette = CreatePalette();
    var assetData = new byte[0x100];
    for (var i = 0; i < 16; i++)
    {
        assetData[0x60 + i] = (byte)i;
    }

    for (var i = 0; i < 4; i++)
    {
        assetData[0x70 + i] = (byte)(0x80 + i);
    }

    var definition = new DlAssetTextureDefinition(
        Index: 7,
        TextureOffset: 0x20,
        Width: 4,
        Height: 4,
        Type: 3,
        PaletteId: 0,
        MipmapPaletteId: 1,
        Padding: 0);

    var texture = DlAssetReader.BuildAssetTexture(
        "moby",
        0,
        definition,
        palette,
        assetData,
        textureDataOffset: 0x40);

    var pif = PifReader.Read(texture.PifBytes);
    Expect(pif.TotalMipLevels == 3, "DL normalized asset texture should store base mip plus mipmaps in PIF");
    Expect(texture.PngBytes.Length > 0, "DL normalized asset texture should generate a PNG preview");
    Expect(texture.Metadata.SourceDefinition is DlAssetTextureDefinition, "texture manifest metadata should retain source table definition");
    Expect(texture.Metadata.MipPixelOffsets.SequenceEqual([0x70, 0x100]), "texture manifest metadata should retain mip source offsets");

    var overlappingPaletteData = new byte[0x500];
    for (var i = 0; i < overlappingPaletteData.Length; i++)
    {
        overlappingPaletteData[i] = (byte)(i & 0xff);
    }

    var paletteStrideTexture = DlAssetReader.BuildAssetTexture(
        "tie",
        0,
        definition with { PaletteId = 1, MipmapPaletteId = -1 },
        overlappingPaletteData,
        assetData,
        textureDataOffset: 0x40);
    var paletteStridePif = PifReader.Read(paletteStrideTexture.PifBytes);
    Expect(paletteStrideTexture.Metadata.PaletteOffset == 0x100, "DL asset palette ids should use 0x100-byte palette WAD stride");
    Expect(
        paletteStridePif.PaletteData.SequenceEqual(overlappingPaletteData.AsSpan(0x100, 0x400).ToArray()),
        "DL asset PIF palette bytes should come from paletteId * 0x100, not paletteId * 0x400");

    var outputDirectory = Path.Combine(Path.GetTempPath(), $"ratchet-ps2-level-texture-{Guid.NewGuid():N}");
    Directory.CreateDirectory(outputDirectory);
    try
    {
        File.WriteAllBytes(Path.Combine(outputDirectory, "tex.0000.pif"), texture.PifBytes);
        File.WriteAllBytes(Path.Combine(outputDirectory, "tex.0000.png"), texture.PngBytes);
        File.WriteAllText(Path.Combine(outputDirectory, "manifest.json"), JsonSerializer.Serialize(new[] { texture.Metadata }));

        var primaryFiles = Directory.EnumerateFiles(outputDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Expect(primaryFiles.SequenceEqual(["manifest.json", "tex.0000.pif", "tex.0000.png"]), "normalized texture output should only create PIF, PNG, and manifest artifacts");
        Expect(primaryFiles.All(name => name is not null
            && !name.EndsWith(".def", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".palette", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)), "normalized texture output should not use def, palette, or numbered mip bin sidecars");

        var manifestJson = File.ReadAllText(Path.Combine(outputDirectory, "manifest.json"));
        Expect(manifestJson.Contains("\"TextureOffset\":32", StringComparison.Ordinal), "manifest should retain original texture table offset");
        Expect(manifestJson.Contains("\"MipmapPaletteId\":1", StringComparison.Ordinal), "manifest should retain mipmap table metadata");
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static byte[] CreateSyntheticIso(
    int levelIndex,
    int headerSector,
    int payloadBaseSector,
    byte[] looseWadBytes,
    bool includePayloads = true)
{
    var iso = new byte[Math.Max(
        DlLevelConstants.RetailLevelInfoTableOffset + (DlLevelConstants.LevelInfoCount * DlLevelConstants.LevelInfoSize),
        ((includePayloads ? payloadBaseSector : headerSector) * DlLevelConstants.SectorSize)
            + (includePayloads ? looseWadBytes.Length : DlLevelConstants.LevelWadHeaderSectorCount * DlLevelConstants.SectorSize))];

    WriteLevelInfoEntry(
        iso,
        levelIndex,
        audio: new DlFileBlock(0, 0),
        level: new DlFileBlock(headerSector, 1),
        scene: new DlFileBlock(0, 0));

    var headerLength = DlLevelConstants.LevelWadHeaderSectorCount * DlLevelConstants.SectorSize;
    looseWadBytes.AsSpan(0, headerLength).CopyTo(iso.AsSpan(headerSector * DlLevelConstants.SectorSize));
    if (includePayloads)
    {
        looseWadBytes.CopyTo(iso.AsSpan(payloadBaseSector * DlLevelConstants.SectorSize));
    }

    return iso;
}

static byte[] CreateSyntheticLooseLevelWad(int payloadBaseSector, bool negativeBlock = false)
{
    var data = new byte[DlLevelConstants.SectorSize * 11];
    WriteInt32(data, 0x00, DlLevelConstants.LevelWadHeaderSize);
    WriteInt32(data, 0x04, payloadBaseSector);
    WriteInt32(data, 0x08, 7);
    WriteInt32(data, 0x0c, 2);
    WriteInt32(data, 0x10, 0x1111);
    WriteInt32(data, 0x14, 0x2222);
    WriteFileBlock(data, 0x18, negativeBlock ? new DlFileBlock(-1, 1) : new DlFileBlock(2, 2));
    WriteFileBlock(data, 0x20, new DlFileBlock(4, 1));
    WriteFileBlock(data, 0x28, new DlFileBlock(5, 1));
    WriteFileBlock(data, 0x40, new DlFileBlock(6, 1));
    WriteFileBlock(data, 0x460, new DlFileBlock(7, 1));
    WriteFileBlock(data, 0x60, new DlFileBlock(8, 1));
    WriteFileBlock(data, 0x468, new DlFileBlock(10, 1));
    WriteFileBlock(data, 0xc60, new DlFileBlock(9, 1));

    var coreLevelBytes = CreateSyntheticCoreLevel();
    coreLevelBytes.CopyTo(data.AsSpan(2 * DlLevelConstants.SectorSize));

    data[4 * DlLevelConstants.SectorSize] = 0x41;
    data[5 * DlLevelConstants.SectorSize] = 0x51;
    data[6 * DlLevelConstants.SectorSize] = 0x61;

    var mission = data.AsSpan(7 * DlLevelConstants.SectorSize, DlLevelConstants.SectorSize);
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x00, 0x40);
    var missionGameplay = BuildGameplayData(
        DlGameplayBlockReader.MissionHeaderSize,
        (0x00, new byte[] { 0xA1, 0xA2 }),
        (0x04, new byte[] { 0xA3, 0xA4 }));
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x04, missionGameplay.Length);
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x08, 0x40 + missionGameplay.Length);
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x0c, 4);
    missionGameplay.CopyTo(mission[0x40..]);
    mission[0x40 + missionGameplay.Length] = 0xB1;
    mission[0x41 + missionGameplay.Length] = 0xB2;
    mission[0x42 + missionGameplay.Length] = 0xB3;
    mission[0x43 + missionGameplay.Length] = 0xB4;

    data[8 * DlLevelConstants.SectorSize] = 0x81;
    data[9 * DlLevelConstants.SectorSize] = 0x91;

    var placeholderOffset = 10 * DlLevelConstants.SectorSize;
    WriteInt32(data, placeholderOffset + 0x00, -1);
    WriteInt32(data, placeholderOffset + 0x04, 0);
    WriteInt32(data, placeholderOffset + 0x08, -1);
    WriteInt32(data, placeholderOffset + 0x0c, 0);

    return data;
}

static byte[] CreateSyntheticCoreLevel()
{
    var world = BuildWorldInstanceData((0x00, new byte[] { 0xD1, 0xD2, 0xD3, 0xD4 }));
    var gameplay = BuildGameplayData(
        DlGameplayBlockReader.CoreHeaderSize,
        (0x00, new byte[] { 0xC1, 0xC2, 0xC3 }),
        (0x04, new byte[] { 0xD1, 0xD2 }));
    var data = new byte[DlLevelConstants.SectorSize * 2];
    WriteFileBlock(data, 0x10, new DlFileBlock(0x100, 4));
    WriteFileBlock(data, 0x58, new DlFileBlock(0x180, world.Length));
    WriteFileBlock(data, 0x60, new DlFileBlock(0x200, gameplay.Length));
    data[0x100] = 1;
    data[0x101] = 2;
    data[0x102] = 3;
    data[0x103] = 4;
    world.CopyTo(data.AsSpan(0x180));
    gameplay.CopyTo(data.AsSpan(0x200));
    return data;
}

static byte[] CreateSyntheticUyaIso(
    int levelIndex,
    int headerSector,
    int payloadBaseSector,
    byte[] looseWadBytes,
    bool includePayloads = true)
{
    var iso = new byte[Math.Max(
        UyaLevelConstants.RetailLevelInfoTableOffset + (UyaLevelConstants.LevelInfoCount * UyaLevelConstants.LevelInfoSize),
        ((includePayloads ? payloadBaseSector : headerSector) * UyaLevelConstants.SectorSize)
            + (includePayloads ? looseWadBytes.Length : UyaLevelConstants.LevelWadHeaderSectorCount * UyaLevelConstants.SectorSize))];

    WriteUyaLevelInfoEntry(
        iso,
        levelIndex,
        audio: new UyaFileBlock(0, 0),
        level: new UyaFileBlock(headerSector, 1),
        scene: new UyaFileBlock(0, 0));

    var headerLength = UyaLevelConstants.LevelWadHeaderSectorCount * UyaLevelConstants.SectorSize;
    looseWadBytes.AsSpan(0, headerLength).CopyTo(iso.AsSpan(headerSector * UyaLevelConstants.SectorSize));
    if (includePayloads)
    {
        looseWadBytes.CopyTo(iso.AsSpan(payloadBaseSector * UyaLevelConstants.SectorSize));
    }

    return iso;
}

static byte[] CreateSyntheticUyaLooseLevelWad(int payloadBaseSector)
{
    var data = new byte[UyaLevelConstants.SectorSize * 9];
    WriteInt32(data, 0x00, UyaLevelConstants.LevelWadHeaderSize);
    WriteInt32(data, 0x04, payloadBaseSector);
    WriteInt32(data, 0x08, 7);
    WriteInt32(data, 0x0c, 2);
    WriteUyaFileBlock(data, 0x10, new UyaFileBlock(3, 2));
    WriteUyaFileBlock(data, 0x18, new UyaFileBlock(1, 1));
    WriteUyaFileBlock(data, 0x20, new UyaFileBlock(5, 1));
    WriteUyaFileBlock(data, 0x28, new UyaFileBlock(6, 1));
    WriteUyaFileBlock(data, 0x30, new UyaFileBlock(7, 1));
    WriteUyaFileBlock(data, 0x48, new UyaFileBlock(8, 1));

    CreateSyntheticUyaLevelData().CopyTo(data.AsSpan(3 * UyaLevelConstants.SectorSize));
    data[1 * UyaLevelConstants.SectorSize] = 0x41;
    CreateSyntheticUyaGameplay().CopyTo(data.AsSpan(5 * UyaLevelConstants.SectorSize));
    data[6 * UyaLevelConstants.SectorSize] = 0x61;
    data[7 * UyaLevelConstants.SectorSize] = 0x71;
    data[8 * UyaLevelConstants.SectorSize] = 0x81;

    return data;
}

static byte[] CreateSyntheticUyaGameplay()
{
    return BuildAlignedGameplayData(
        UyaGameplayBlockReader.CoreHeaderSize,
        (0x00, [0xA1, 0xA2]),
        (0x04, [0xB1, 0xB2]),
        (0x10, [0xD1, 0xD2]),
        (0x4c, [0xC1, 0xC2]),
        (0x78, [0xE1, 0xE2]),
        (0x7c, [0, 0, 0, 0, 0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xF1, 0xF2]));
}

static byte[] BuildAlignedGameplayData(int headerSize, params (int HeaderOffset, byte[] Payload)[] blocks)
{
    var length = blocks.Aggregate(Align16(headerSize),
        (offset, block) => Align16(checked(offset + block.Payload.Length)));
    var data = new byte[length];
    var offset = Align16(headerSize);
    foreach (var block in blocks)
    {
        WriteInt32(data, block.HeaderOffset, offset);
        block.Payload.CopyTo(data.AsSpan(offset));
        offset = Align16(checked(offset + block.Payload.Length));
    }
    return data;
}

static int Align16(int value) => checked((value + 0xf) & ~0xf);

static byte[] CreateSyntheticUyaLevelData()
{
    var data = new byte[UyaLevelConstants.SectorSize * 2];
    WriteByteBlock(data, 0x00, new UyaByteBlock(0x80, 4));
    WriteByteBlock(data, 0x08, new UyaByteBlock(0x90, 4));
    WriteByteBlock(data, 0x10, new UyaByteBlock(0xa0, 4));
    WriteByteBlock(data, 0x18, new UyaByteBlock(0xb0, 4));
    WriteByteBlock(data, 0x20, new UyaByteBlock(0xc0, 4));
    WriteByteBlock(data, 0x48, new UyaByteBlock(0xd0, 4));
    WriteByteBlock(data, 0x50, new UyaByteBlock(0xe0, 4));

    new byte[] { 0x11, 0x12, 0x13, 0x14 }.CopyTo(data.AsSpan(0x80));
    new byte[] { 0x21, 0x22, 0x23, 0x24 }.CopyTo(data.AsSpan(0x90));
    new byte[] { 0x31, 0x32, 0x33, 0x34 }.CopyTo(data.AsSpan(0xa0));
    new byte[] { 0x41, 0x42, 0x43, 0x44 }.CopyTo(data.AsSpan(0xb0));
    new byte[] { 0x51, 0x52, 0x53, 0x54 }.CopyTo(data.AsSpan(0xc0));
    new byte[] { 0x61, 0x62, 0x63, 0x64 }.CopyTo(data.AsSpan(0xd0));
    new byte[] { 0x71, 0x72, 0x73, 0x74 }.CopyTo(data.AsSpan(0xe0));

    return data;
}

static byte[] CreatePalette()
{
    var palette = new byte[0x400];
    for (var i = 0; i < 256; i++)
    {
        palette[(i * 4) + 0] = (byte)i;
        palette[(i * 4) + 1] = (byte)(255 - i);
        palette[(i * 4) + 2] = (byte)(i / 2);
        palette[(i * 4) + 3] = 0x80;
    }

    return palette;
}

static void WriteLevelInfoEntry(byte[] data, int levelIndex, DlFileBlock audio, DlFileBlock level, DlFileBlock scene)
{
    var offset = DlLevelConstants.RetailLevelInfoTableOffset + (levelIndex * DlLevelConstants.LevelInfoSize);
    WriteFileBlock(data, offset + 0x00, audio);
    WriteFileBlock(data, offset + 0x08, level);
    WriteFileBlock(data, offset + 0x10, scene);
}

static void WriteUyaLevelInfoEntry(byte[] data, int levelIndex, UyaFileBlock audio, UyaFileBlock level, UyaFileBlock scene)
{
    var offset = UyaLevelConstants.RetailLevelInfoTableOffset + (levelIndex * UyaLevelConstants.LevelInfoSize);
    WriteUyaFileBlock(data, offset + 0x00, audio);
    WriteUyaFileBlock(data, offset + 0x08, level);
    WriteUyaFileBlock(data, offset + 0x10, scene);
}

static void WriteFileBlock(byte[] data, int offset, DlFileBlock block)
{
    WriteInt32(data, offset, block.Offset);
    WriteInt32(data, offset + 4, block.Length);
}

static void WriteUyaFileBlock(byte[] data, int offset, UyaFileBlock block)
{
    WriteInt32(data, offset, block.Offset);
    WriteInt32(data, offset + 4, block.Length);
}

static void WriteByteBlock(byte[] data, int offset, UyaByteBlock block)
{
    WriteInt32(data, offset, block.Offset);
    WriteInt32(data, offset + 4, block.Length);
}

static void WriteSectorRange(byte[] data, int offset, int rangeOffset, int rangeLength)
{
    WriteInt32(data, offset, rangeOffset);
    WriteInt32(data, offset + 4, rangeLength);
}

static void WriteInt32(byte[] data, int offset, int value)
{
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);
}

static void WriteUInt32(byte[] data, int offset, uint value)
{
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
}

static void WriteInt16(byte[] data, int offset, short value)
{
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, sizeof(short)), value);
}

static void WriteUInt16(byte[] data, int offset, ushort value)
{
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), value);
}

static void WriteSingle(byte[] data, int offset, float value)
{
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(float)), BitConverter.SingleToInt32Bits(value));
}

static byte[] BuildWorldInstanceData(params (int HeaderOffset, byte[] Payload)[] slots)
{
    var length = DlWorldInstanceReader.PointerTableLength + slots.Sum(slot => slot.Payload.Length);
    var data = new byte[length];
    var offset = DlWorldInstanceReader.PointerTableLength;

    foreach (var slot in slots)
    {
        WriteInt32(data, slot.HeaderOffset, offset);
        slot.Payload.CopyTo(data.AsSpan(offset));
        offset += slot.Payload.Length;
    }

    return data;
}

static byte[] BuildGameplayData(int headerSize, params (int HeaderOffset, byte[] Payload)[] blocks)
{
    var length = headerSize + blocks.Sum(block => block.Payload.Length);
    var data = new byte[length];
    var offset = headerSize;

    foreach (var block in blocks)
    {
        WriteInt32(data, block.HeaderOffset, offset);
        block.Payload.CopyTo(data.AsSpan(offset));
        offset += block.Payload.Length;
    }

    return data;
}

static byte[] CreateLiteralWad(byte[] payload)
{
    var data = new byte[0x10 + 1 + payload.Length];
    data[0] = 0x57;
    data[1] = 0x41;
    data[2] = 0x44;
    WriteInt32(data, 3, data.Length);
    data[0x10] = (byte)(payload.Length - 3);
    payload.CopyTo(data.AsSpan(0x11));
    return data;
}

static byte[] CreateChunkWad(byte[] terrainPayload)
{
    var data = new byte[0x10 + terrainPayload.Length];
    WriteInt32(data, 0x00, 0x10);
    terrainPayload.CopyTo(data.AsSpan(0x10));
    return data;
}

static void AddZipEntry(ZipArchive archive, string path, byte[] bytes)
{
    var entry = archive.CreateEntry(path);
    using var output = entry.Open();
    output.Write(bytes);
}

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void ExpectThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
