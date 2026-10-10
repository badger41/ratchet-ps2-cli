using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RatchetPs2.Core.Fx;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gameplay;
using RatchetPs2.Core.Hud;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Shrubs;
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
using RatchetPs2.Games.DL.Hud;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.DL.Moby;
using RatchetPs2.Games.DL.Online;
using RatchetPs2.Games.GC.Gameplay;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.GC.Skyboxes;
using RatchetPs2.Games.RC1.Gameplay;
using RatchetPs2.Games.RC1.Level;
using RatchetPs2.Games.RC1.Ties;
using RatchetPs2.Games.UYA.Collision;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Hud;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Sdk;

if (args is ["--dl-boot-iso"])
{
    DlBootIsoQualification.RunUnitTests();
    return;
}

if (args is ["--verify-dl-boot-iso", var bootSourceIso, var bootOutputIso, var bootElfPath])
{
    DlBootIsoQualification.RunUnitTests();
    DlBootIsoQualification.VerifyFiles(bootSourceIso, bootOutputIso, bootElfPath);
    return;
}

if (args is ["--dl-executables"])
{
    DlExecutableQualification.RunUnitTests();
    return;
}

if (args is ["--qualify-dl-executables", var dlIsoPath, var dlReferencePath])
{
    DlExecutableQualification.RunUnitTests();
    DlExecutableQualification.CompareReference(dlIsoPath, dlReferencePath);
    return;
}

if (args is ["--qualify-uya-iso", var isoPath, var reportPath])
{
    var report = UyaArchiveQualification.Run(isoPath);
    UyaArchiveQualification.Write(report, reportPath);
    Console.WriteLine($"Qualified {report.PassedLevelCount}/{report.LevelCount} UYA levels: {reportPath}");
    Environment.ExitCode = report.PassedLevelCount == report.LevelCount ? 0 : 1;
    return;
}

if (args is ["--qualify-uya-collision", var collisionIsoPath, var collisionReportPath])
{
    var report = UyaCollisionQualification.Run(collisionIsoPath);
    UyaCollisionQualification.Write(report, collisionReportPath);
    Console.WriteLine($"Qualified collision in {report.PassedLevelCount}/{report.LevelCount} UYA levels: {collisionReportPath}");
    Environment.ExitCode = report.PassedLevelCount == report.LevelCount ? 0 : 1;
    return;
}

if (args is ["--qualify-uya-tie-collision", var tieRootPath, var tieReportPath])
{
    var report = UyaTieCollisionQualification.Run(tieRootPath);
    UyaTieCollisionQualification.Write(report, tieReportPath);
    Console.WriteLine($"Qualified {report.LodCount} TIE LODs across {report.TieCount} assets: {tieReportPath}");
    Environment.ExitCode = report.Succeeded ? 0 : 1;
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

if (args.Contains("--uya-level-asset-composer", StringComparer.Ordinal))
{
    ValidateUyaLevelAssetComposer();
    Console.WriteLine("UYA level asset composer tests passed.");
    return;
}

if (args.Contains("--uya-texture-inventory", StringComparer.Ordinal))
{
    ValidateTextureInventory();
    ValidatePaletteOptimization();
    ValidateUyaStaticAssetComposition();
    ValidateNormalizedTextureArtifacts();
    Console.WriteLine("UYA texture inventory and palette optimizer tests passed.");
    return;
}

if (args.Contains("--alpha-aware-kmeans", StringComparer.Ordinal))
{
    ValidateAlphaAwareKMeans();
    Console.WriteLine("Alpha-aware K-means compatibility tests passed.");
    return;
}

if (args.Contains("--joint-palette", StringComparer.Ordinal))
{
    ValidateJointPaletteOptimization();
    Console.WriteLine("Joint imported/custom palette optimization tests passed.");
    return;
}

if (args.Contains("--hud-contract", StringComparer.Ordinal))
{
    ValidateHudBankParsing();
    ValidateHudSpriteIds();
    ValidateHudBankComposition();
    Console.WriteLine("HUD bank, sprite-ID, and composition contract tests passed.");
    return;
}

if (args.Contains("--fx-contract", StringComparer.Ordinal))
{
    ValidateFxTextureCatalog();
    ValidateFxTextureComposition();
    Console.WriteLine("FX texture inventory, label, and composition contract tests passed.");
    return;
}

if (args.Contains("--uya-static-instances", StringComparer.Ordinal))
{
    ValidateUyaGameplayTypedParsing();
    ValidateGameplayPvarTables();
    ValidateGameplayPvarTablesWhenAvailable();
    ValidateUyaMobyInstanceContract();
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

if (args.Contains("--uya-collision", StringComparer.Ordinal))
{
    ValidateUyaCollisionParsingAndGltf();
    ValidateUyaTieCollisionSurfaceGeneration();
    ValidateUyaTieCollisionConvexHullGeneration();
    ValidateUyaShrubCollisionGeneration();
    Console.WriteLine("UYA collision and instanced collision generation checks passed.");
    return;
}

if (args.Contains("--uya-inventory", StringComparer.Ordinal))
{
    ValidateUyaLevelWadInventory();
    ValidateUyaLevelWadInventoryWhenAvailable();
    Console.WriteLine("UYA level WAD inventory tests passed.");
    return;
}

DlExecutableQualification.RunUnitTests();
DlBootIsoQualification.RunUnitTests();
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
ValidateAlphaAwareKMeans();
ValidateJointPaletteOptimization();
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
ValidateGameplayPvarTables();
ValidateGameplayPvarTablesWhenAvailable();
ValidateUyaMobyInstanceContract();
ValidateUyaCollisionParsingAndGltf();
ValidateUyaTieCollisionSurfaceGeneration();
ValidateUyaTieCollisionConvexHullGeneration();
ValidateUyaShrubCollisionGeneration();
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
ValidateHudSpriteIds();
ValidateHudBankComposition();
ValidateFxTextureCatalog();
ValidateFxTextureComposition();
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

    var texture = LevelAssetReader.BuildAssetTexture(
        "rc1",
        0,
        new LevelAssetTextureDefinition(0, 0, 2, 2, 0, 0, 0, -1),
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
    var expectedHeader = new byte[0x90];
    expectedHeader[0x20] = 0x55;
    var repackedHeader = expectedHeader.Concat(new byte[16]).ToArray();
    WriteInt32(repackedHeader, 0x88, 0x1234);
    WriteInt32(repackedHeader, 0x8c, 0x5678);
    Expect(UyaLevelWadValidator.EquivalentAssetHeader(expectedHeader, repackedHeader),
        "UYA asset-header comparison should allow recalculated size fields and alignment padding");
    repackedHeader[0x20] ^= 0xff;
    Expect(!UyaLevelWadValidator.EquivalentAssetHeader(expectedHeader, repackedHeader),
        "UYA asset-header comparison should reject changes outside recalculated size fields");

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

    var alignedBytes = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    var levelDataOffset = 3 * UyaLevelConstants.SectorSize;
    alignedBytes.AsSpan(levelDataOffset, 2 * UyaLevelConstants.SectorSize).Clear();
    foreach (var (headerOffset, payloadOffset, marker) in new[]
        {
            (0x00, 0x080, (byte)0x11),
            (0x08, 0x0c0, (byte)0x21),
            (0x10, 0x100, (byte)0x31),
            (0x18, 0x140, (byte)0x41),
            (0x20, 0x180, (byte)0x51),
            (0x48, 0x1c0, (byte)0x61),
            (0x50, 0x200, (byte)0x71),
        })
    {
        WriteByteBlock(alignedBytes, levelDataOffset + headerOffset, new(payloadOffset, 4));
        alignedBytes.AsSpan(levelDataOffset + payloadOffset, 4).Fill(marker);
    }
    var alignedRewrite = UyaLevelWadWriter.Write(
        UyaLevelWadInventoryReader.Read(alignedBytes),
        new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["assets/asset_header.bin"] = new byte[0x52],
        });
    var alignedLevelData = UyaLevelWadInventoryReader.Read(alignedRewrite).Containers
        .Single(container => container.Path == "level_wad/level_data.wad");
    Expect(alignedLevelData.Slots.Where(slot => slot.Length > 0)
            .All(slot => slot.Offset % 0x40 == 0),
        "UYA level-data replacements should realign every following native byte block to 0x40");

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

    var replacementAssetHeader = new byte[0x90];
    var replacementAssetWad = Enumerable.Repeat((byte)0x5a, 96).ToArray();
    var changedAssets = LevelArchiveBuilder.Build(GameId.UYA, source,
        new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["assets/asset_header.bin"] = replacementAssetHeader,
            ["assets/asset_wad_payload.bin"] = replacementAssetWad,
        });
    Expect(changedAssets.Succeeded && changedAssets.OutputBytes is not null,
        "UYA SDK archive workflow should accept composed asset replacements");
    var changedAssetInventory = UyaLevelWadInventoryReader.Read(changedAssets.OutputBytes);
    var changedLevelData = changedAssetInventory.Containers.Single(value => value.Path == "level_wad/level_data.wad");
    var packedAssetHeader = changedLevelData.Slots.Single(value => value.Path == "assets/asset_header.bin").Bytes.Span;
    var packedAssetWad = changedLevelData.Slots.Single(value => value.Path == "assets/asset_wad.bin");
    Expect(BinaryPrimitives.ReadInt32LittleEndian(packedAssetHeader[0x88..]) == packedAssetWad.Length
        && BinaryPrimitives.ReadInt32LittleEndian(packedAssetHeader[0x8c..]) == replacementAssetWad.Length,
        "UYA archive packing should publish the final compressed and decompressed asset WAD sizes");

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
    WriteInt32(header, 0x7c, assets.Length);
    WriteInt32(header, 0x8c, assets.Length);
    assets.AsSpan(0x20, 0x20).Fill(0x11);
    assets.AsSpan(0x40, 0x20).Fill(0x22);
    assets.AsSpan(0x60, 0x20).Fill(0x33);
    assets.AsSpan(0x80, 0x20).Fill(0x44);
    var terrain = Enumerable.Repeat((byte)0xaa, 0x31).ToArray();
    var collision = Enumerable.Repeat((byte)0xcc, 0x11).ToArray();

    var composed = LevelAssetComposer.ComposeAssetWad(
        GameId.UYA, header, assets, new(Terrain: terrain, Collision: collision));
    var composedHeader = LevelAssetReader.ReadHeader(composed.HeaderBytes);
    var moby = LevelAssetReader.ReadModelDefinitions(
        composed.HeaderBytes, composedHeader.MobyModelOffset, composedHeader.MobyModelCount).Single();
    Expect(composedHeader.TerrainOffset == 0x20
        && composedHeader.SkyOffset == 0x60
        && composedHeader.CollisionOffset == 0x80
        && moby.ModelOffset == 0xa0
        && composedHeader.SceneViewSize == composed.AssetWadBytes.Length
        && composedHeader.DecompressedSize == composed.AssetWadBytes.Length,
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
    var forcedInPlace = IsoPatchPlanner.Create(
        GameId.UYA, stream, levelIndex, output, forceInPlace: true);
    Expect(forcedInPlace.FitsInPlace
        && forcedInPlace.Replacement is null
        && forcedInPlace.Ranges.Any(value => value.Name == "level-info")
        && forcedInPlace.StrategyReason.Contains("overwrite", StringComparison.Ordinal),
        "UYA ISO patch planning should explicitly permit a dangerous oversized in-place patch");
    using var forcedInPlaceStream = new MemoryStream(iso.ToArray(), writable: true);
    for (var index = 0; index < forcedInPlace.Ranges.Count; index++)
        IsoPatchApplier.ApplyRange(GameId.UYA, forcedInPlaceStream, forcedInPlace, index);
    IsoPatchApplier.VerifyInstalledLevel(GameId.UYA, forcedInPlaceStream, forcedInPlace);
    Expect(UyaLevelInfoReader.ReadEntry(forcedInPlaceStream, levelIndex).LevelWad.Length == 10,
        "UYA oversized in-place patch should publish the expanded level-table length");
    stream.Position = 0;
    ExpectThrows<ArgumentException>(() => IsoPatchPlanner.Create(
        GameId.UYA, stream, levelIndex, output, forceFullImage: true, forceInPlace: true));
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

static void ValidateAlphaAwareKMeans()
{
    // Captured from deadlocked-level-packer's MIT-licensed PngQuantizer baseline.
    var pixels = new[]
    {
        new TextureColor(240, 30, 20, 255),
        new TextureColor(240, 30, 20, 255),
        new TextureColor(240, 30, 20, 255),
        new TextureColor(220, 50, 30, 224),
        new TextureColor(20, 220, 40, 255),
        new TextureColor(20, 220, 40, 255),
        new TextureColor(40, 200, 60, 160),
        new TextureColor(30, 40, 230, 255),
        new TextureColor(30, 40, 230, 255),
        new TextureColor(60, 70, 210, 96),
        new TextureColor(250, 240, 30, 192),
        new TextureColor(210, 180, 50, 128),
        new TextureColor(200, 20, 210, 64),
        new TextureColor(10, 240, 230, 32),
        new TextureColor(255, 255, 255, 8),
        new TextureColor(255, 0, 0, 0),
        new TextureColor(0, 255, 0, 0),
        new TextureColor(0, 0, 0, 0),
    };
    var expectedPalette = new[]
    {
        new TextureColor(20, 220, 40, 255),
        new TextureColor(70, 93, 177, 125),
        new TextureColor(238, 68, 24, 236),
        new TextureColor(0, 0, 0, 0),
    };
    var expectedIndices = new[] { 2, 2, 2, 2, 0, 0, 1, 1, 1, 1, 2, 1, 1, 3, 3, 3, 3, 3 };

    var quantized = AlphaAwareKMeans.Quantize(pixels, 4);
    Expect(quantized.Palette.SequenceEqual(expectedPalette)
        && quantized.Indices.SequenceEqual(expectedIndices),
        "alpha-aware K-means should match the deadlocked-level-packer golden output");
    for (var iteration = 0; iteration < 10; iteration++)
    {
        var repeated = AlphaAwareKMeans.Quantize(pixels, 4);
        Expect(repeated.Palette.SequenceEqual(expectedPalette)
            && repeated.Indices.SequenceEqual(expectedIndices),
            "alpha-aware K-means should be deterministic");
    }

    var exact = AlphaAwareKMeans.Quantize([
        new(10, 20, 30, 255),
        new(1, 2, 3, 0),
        new(40, 50, 60, 128),
    ], 4);
    Expect(exact.Palette.SequenceEqual([
            new(10, 20, 30, 255),
            new(40, 50, 60, 128),
            new(0, 0, 0, 0),
            new(0, 0, 0, 0),
        ])
        && exact.Indices.SequenceEqual([0, 2, 1]),
        "alpha-aware K-means should reserve transparent and sort padded entries last");

    var oneColor = AlphaAwareKMeans.Quantize([new(200, 100, 50, 0)], 1);
    Expect(oneColor.Palette.SequenceEqual([new TextureColor(200, 100, 50, 0)])
        && oneColor.Indices.SequenceEqual([0]),
        "a one-entry baseline palette should retain its transparent RGB value");
    var empty = AlphaAwareKMeans.Quantize([], 2);
    Expect(empty.Palette.SequenceEqual([new TextureColor(0, 0, 0, 0), new(0, 0, 0, 0)])
        && empty.Indices.Count == 0,
        "empty input should produce a padded empty palette");
    ExpectThrows<ArgumentOutOfRangeException>(() => AlphaAwareKMeans.Quantize(pixels, 0));
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    ExpectThrows<OperationCanceledException>(() =>
        AlphaAwareKMeans.Quantize(pixels, 4, cancellation.Token));

    var pif = PifWriter.Write(PifWriter.CreateIndexed8(2, 2, new byte[0x400], [0, 1, 2, 3]));
    var imported = Indexed8PifImporter.Convert("pif", pif, 4, "test");
    Expect(imported is { Width: 2, Height: 2 } && imported.PifBytes.SequenceEqual(pif),
        "indexed-8 PIF import should canonicalize an eligible PIF without changing its pixels");
    ExpectThrows<InvalidDataException>(() => Indexed8PifImporter.Convert("pif", pif, 1, "test"));
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

static void ValidateJointPaletteOptimization()
{
    var importedRed = new TextureColor(255, 0, 0, 255);
    var importedTexture = SyntheticInventoryTexture("imported-red", [importedRed], capacity: 4);
    var imported = new TextureInventory(1, [importedTexture], 1);
    var nearRed = new TextureColor(254, 0, 0, 255);
    var customPixels = new[]
    {
        nearRed, nearRed, nearRed,
        new TextureColor(0, 255, 0, 255),
        new TextureColor(0, 0, 255, 255),
        new TextureColor(255, 255, 0, 255),
    };
    var custom = new CustomTexturePaletteInput(
        "custom", PifTextureEncoding.Indexed4, 0, 0, 4, customPixels);

    var fidelity = PaletteOptimizer.Optimize(
        imported, [custom], new(PaletteOptimizationProfile.CurrentMappingVersion, 0));
    Expect(fidelity.ReuseThreshold == 0
        && fidelity.Palettes.Count == 2
        && fidelity.CustomAssignments.Single() is
        {
            PaletteIndex: 1,
            MeanSquaredError: 0,
            MaximumSquaredError: 0,
            ReusedImportedColorCount: 0,
            ReusedImportedTexelCount: 0,
        }, "visual-fidelity strength should allocate an exact custom palette when fixed slots are insufficient");

    var reuse = PaletteOptimizer.Optimize(
        imported, [custom], new(PaletteOptimizationProfile.CurrentMappingVersion, 100));
    var customAssignment = reuse.CustomAssignments.Single();
    Expect(reuse.ReuseThreshold == PaletteOptimizationProfile.MaximumError
        && reuse.Palettes.Count == 1
        && customAssignment.PaletteIndex == 0
        && customAssignment.ReusedImportedColorCount == 1
        && customAssignment.ReusedImportedTexelCount == 3
        && customAssignment.NewPaletteEntryCount == 3
        && customAssignment.MeanSquaredError > 0
        && customAssignment.MaximumSquaredError <= reuse.ReuseThreshold,
        "VRAM-savings strength should reuse a sufficiently close imported centroid");
    Expect(reuse.Palettes[0].Entries.Single(value => value.Color == importedRed).Color == importedRed
        && reuse.ImportedAssignments.Single().IndexRemaps.Single().Color == importedRed,
        "joint optimization must preserve imported colors and remaps exactly");
    Expect(reuse.CustomAssignments.Single().PixelIndices.Take(3).All(value =>
            reuse.Palettes[0].Entries.Single(entry => entry.PaletteIndex == value).Color == importedRed),
        "custom texels should remap to the selected fixed imported color");
    Expect(reuse.CustomAssignments.Single().PixelIndices.Count == customPixels.Length,
        "selected palette evaluation should materialize one index per custom texel");

    var middle = new PaletteOptimizationProfile(PaletteOptimizationProfile.CurrentMappingVersion, 50);
    Expect(Math.Abs(middle.ReuseThreshold - 0.0125) < 1e-12,
        "paletteOptimization.v1 strength should use the documented quadratic threshold");

    var manyColors = Enumerable.Range(0, 32)
        .Select(value => new TextureColor((byte)(value * 7), (byte)(255 - value * 3), (byte)(value * 5), 255))
        .ToArray();
    var quantizedInput = new CustomTexturePaletteInput(
        "quantized", PifTextureEncoding.Indexed4, 0, 0, 4, manyColors);
    var quantized = PaletteOptimizer.Optimize(
        new TextureInventory(1, [], 0), [quantizedInput], PaletteOptimizationProfile.Default);
    var repeated = PaletteOptimizer.Optimize(
        new TextureInventory(1, [], 0), [quantizedInput], PaletteOptimizationProfile.Default);
    Expect(quantized.Palettes.Single().Entries.Count <= 4
        && quantized.CustomAssignments.Single().OutputDistinctColorCount <= 4
        && quantized.CustomAssignments.Single().MaximumSquaredError > 0,
        "custom textures above capacity should use bounded alpha-aware quantization");
    Expect(JsonSerializer.SerializeToUtf8Bytes(quantized)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(repeated)),
        "joint custom palette optimization should be deterministic");

    var orderedCustom = new CustomTexturePaletteInput(
        "z-custom", PifTextureEncoding.Indexed4, 0, 0, 4, [new(120, 40, 200, 255)]);
    var forward = PaletteOptimizer.Optimize(imported, [custom, orderedCustom], PaletteOptimizationProfile.Default);
    var reverse = PaletteOptimizer.Optimize(imported, [orderedCustom, custom], PaletteOptimizationProfile.Default);
    Expect(JsonSerializer.SerializeToUtf8Bytes(forward)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(reverse)),
        "joint custom palette optimization should be independent of input order");

    var warningInput = new CustomTexturePaletteInput(
        "warning", PifTextureEncoding.Indexed4, 0, 0, 1,
        [new(0, 0, 0, 255), new(255, 255, 255, 255)]);
    var warning = PaletteOptimizer.Optimize(
        new TextureInventory(1, [], 0), [warningInput], PaletteOptimizationProfile.Default);
    Expect(warning.CustomAssignments.Single().MaximumSquaredError
            >= PaletteOptimizationProfile.MaximumError
        && warning.Warnings.Single().Contains("warning", StringComparison.Ordinal),
        "joint optimization should warn when quantization reaches the maximum error bound");

    var transparent = new TextureColor(0, 0, 0, 0);
    var reservedInput = new CustomTexturePaletteInput(
        "reserved", PifTextureEncoding.Indexed4, 0, 0, 4,
        [transparent, new(20, 30, 40, 255)],
        [new OptimizedPaletteEntry(3, transparent, true)]);
    var reserved = PaletteOptimizer.Optimize(
        new TextureInventory(1, [], 0), [reservedInput], PaletteOptimizationProfile.Default);
    Expect(reserved.Palettes.Single().Entries.Single(value => value.PaletteIndex == 3) is
        { Color.Alpha: 0, Reserved: true }
        && reserved.CustomAssignments.Single().PixelIndices[0] == 3,
        "joint optimization should preserve required transparent indexes");

    ExpectThrows<ArgumentException>(() => PaletteOptimizer.Optimize(
        imported, [custom], new("paletteOptimization.v2", 50)));
    ExpectThrows<ArgumentOutOfRangeException>(() => PaletteOptimizer.Optimize(
        imported, [custom], new(PaletteOptimizationProfile.CurrentMappingVersion, 101)));
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    ExpectThrows<OperationCanceledException>(() => PaletteOptimizer.Optimize(
        imported, [custom], PaletteOptimizationProfile.Default, cancellation.Token));
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
    var header = LevelAssetReader.ReadHeader(composed.HeaderBytes);
    Expect(header is { MobyModelCount: 2, TieModelCount: 1, ShrubModelCount: 1 }
        && header is { MobyTextureCount: 1, TieTextureCount: 1, ShrubTextureCount: 1 }
        && composed.Optimization.Palettes.Count == 1,
        "UYA static composition should install all selected classes and share their exact palette");
    var mobys = LevelAssetReader.ReadModelDefinitions(composed.HeaderBytes, header.MobyModelOffset, 2);
    var moby = mobys[0];
    var tie = LevelAssetReader.ReadModelDefinitions(composed.HeaderBytes, header.TieModelOffset, 1).Single();
    var shrub = LevelAssetReader.ReadShrubDefinitions(composed.HeaderBytes, header.ShrubModelOffset, 1).Single();
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

    var soundHeader = new byte[0x180];
    WriteInt32(soundHeader, 0x18, 3);
    WriteInt32(soundHeader, 0x1c, 0xc0);
    WriteInt32(soundHeader, 0x70, 0x120);
    WriteInt32(soundHeader, 0xc4, 0x100);
    WriteInt32(soundHeader, 0xe4, 0x200);
    WriteInt32(soundHeader, 0x104, 0x300);
    var sourceRemap = new byte[0x60];
    WriteInt16(sourceRemap, 0x00, 0x10);
    WriteInt16(sourceRemap, 0x02, 1);
    WriteInt16(sourceRemap, 0x04, 0x30);
    WriteInt16(sourceRemap, 0x06, 2);
    WriteInt16(sourceRemap, 0x08, 0x38);
    WriteInt16(sourceRemap, 0x0a, 2);
    WriteInt16(sourceRemap, 0x0c, 0x40);
    WriteInt16(sourceRemap, 0x0e, 1);
    sourceRemap.AsSpan(0x10, 0x20).Fill(0x5a);
    WriteInt32(sourceRemap, 0x30, 0x1111);
    WriteInt32(sourceRemap, 0x34, 0x2222);
    WriteInt32(sourceRemap, 0x38, 0xaaaa);
    WriteInt32(sourceRemap, 0x3c, 0xbbbb);
    WriteInt32(sourceRemap, 0x40, 0xcccc);
    sourceRemap.CopyTo(soundHeader, 0x120);
    var soundInputs = new[]
    {
        new StaticAssetInput("existing", TextureAssetFamily.Moby, 0x100,
            StaticDefinition(TextureAssetFamily.Moby, 0), ReadOnlyMemory<byte>.Empty, []),
        new StaticAssetInput("previously-added", TextureAssetFamily.Moby, 0x300,
            StaticDefinition(TextureAssetFamily.Moby, 0), ReadOnlyMemory<byte>.Empty, []),
        new StaticAssetInput("new", TextureAssetFamily.Moby, 0x400,
            StaticDefinition(TextureAssetFamily.Moby, 0), ReadOnlyMemory<byte>.Empty, []),
    };
    var soundComposition = StaticAssetComposer.Compose(GameId.UYA, soundHeader, [], [], soundInputs);
    var soundOutputHeader = LevelAssetReader.ReadHeader(soundComposition.HeaderBytes);
    var outputRemap = soundComposition.HeaderBytes.AsSpan(soundOutputHeader.SoundRemapOffset);
    var existingSoundOffset = BinaryPrimitives.ReadInt16LittleEndian(outputRemap[8..]);
    Expect(BinaryPrimitives.ReadInt16LittleEndian(outputRemap[10..]) == 2
        && outputRemap.Slice(existingSoundOffset, 8).SequenceEqual(sourceRemap.AsSpan(0x38, 8))
        && outputRemap.Slice(12, 8).SequenceEqual(new byte[8])
        && outputRemap.Slice(0x14, 0x20).SequenceEqual(sourceRemap.AsSpan(0x10, 0x20))
        && outputRemap.Slice(0x34, 8).SequenceEqual(sourceRemap.AsSpan(0x30, 8)),
        "UYA static composition should remap retained moby sounds and leave added mobys empty");

    var teamPalette = new byte[0x400];
    new byte[] { 90, 80, 70, 60 }.CopyTo(teamPalette, 5 * 4);
    new byte[] { 1, 2, 3, 4 }.CopyTo(teamPalette, 16 * 4);
    var teamModel = new MobyModel
    {
        HighLodMeshCount = 1,
        TeamPalettes = 0x11,
        MeshTable = new MobyMeshTable()
    };
    teamModel.MeshTable.Entries.Add(new MobyMeshTableEntry
    {
        MeshType = MobyMeshType.HighLod,
        VifData = [],
        VertexData = []
    });
    teamModel.TeamPaletteData.Add(0, [teamPalette]);
    var teamModelBytes = MobyModelPacker.Build(teamModel);
    var teamInput = new StaticAssetInput(
        "team-moby", TextureAssetFamily.Moby, 0x103, StaticDefinition(TextureAssetFamily.Moby, 0x14),
        teamModelBytes, [new(TextureRole.Material, sourcePif)]);
    var teamComposition = StaticAssetComposer.Compose(
        GameId.UYA, new byte[0xc0], new byte[0x10], [], [teamInput]);
    var teamHeader = LevelAssetReader.ReadHeader(teamComposition.HeaderBytes);
    var teamDefinition = LevelAssetReader.ReadModelDefinitions(
        teamComposition.HeaderBytes, teamHeader.MobyModelOffset, 1).Single();
    using var teamStream = new MemoryStream(teamComposition.AssetWadBytes[
        teamDefinition.ModelOffset..teamHeader.SceneViewSize]);
    var rewrittenTeamModel = MobyModelReader.Read(teamStream, new() { SkipAnimationSequences = true });
    var rewrittenTeamPalette = rewrittenTeamModel.TeamPaletteData[0].Single();
    Expect(rewrittenTeamPalette.AsSpan(16 * 4, 4).SequenceEqual(new byte[] { 1, 2, 3, 4 })
        && rewrittenTeamPalette.AsSpan(5 * 4, 4).SequenceEqual(new byte[4])
        && teamComposition.Optimization.Assignments.Single().IndexRemaps
            .Single(value => value.SourcePaletteIndex == 16).TargetPaletteIndex == 16,
        "UYA static composition should preserve referenced team indexes and rewrite embedded palettes");

    var compactHeader = new byte[0x170];
    WriteInt32(compactHeader, 0x00, 2);
    WriteInt32(compactHeader, 0x04, 0x100);
    WriteInt32(compactHeader, 0x18, 1);
    WriteInt32(compactHeader, 0x1c, 0xc0);
    WriteInt32(compactHeader, 0x30, 1);
    WriteInt32(compactHeader, 0x34, 0xe0);
    WriteInt32(compactHeader, 0x38, 2);
    WriteInt32(compactHeader, 0x3c, 0x150);
    WriteInt32(compactHeader, 0x60, 0x20);
    WriteInt32(compactHeader, 0x64, 0x40);
    WriteInt32(compactHeader, 0x68, 0x50);
    WriteInt32(compactHeader, 0x7c, 0xa0);
    WriteInt32(compactHeader, 0x84, 1);
    WriteInt32(compactHeader, 0x88, 0x77);
    WriteInt32(compactHeader, 0x8c, 0xa0);
    WriteInt32(compactHeader, 0xa0, 0x60);
    WriteInt32(compactHeader, 0xac, 0x130);
    WriteInt32(compactHeader, 0xc0, 0x70);
    WriteInt32(compactHeader, 0xc4, 0x100);
    compactHeader.AsSpan(0xd0, 0x10).Fill(byte.MaxValue);
    compactHeader[0xd0] = 1;
    WriteInt16(compactHeader, 0xe4, 4);
    WriteInt16(compactHeader, 0xe6, 4);
    WriteInt16(compactHeader, 0xe8, 3);
    WriteInt16(compactHeader, 0xec, -1);
    WriteInt32(compactHeader, 0x110, 0);
    WriteInt32(compactHeader, 0x118, 0x800);
    WriteInt32(compactHeader, 0x11c, 0x800);
    WriteInt32(compactHeader, 0x120, 0x13);
    WriteInt16(compactHeader, 0x124, 4);
    WriteInt16(compactHeader, 0x126, 4);
    WriteInt32(compactHeader, 0x128, 0xc00);
    WriteInt16(compactHeader, 0x130, 0x100);
    WriteInt16(compactHeader, 0x132, -1);
    WriteInt16(compactHeader, 0x158, 1);
    WriteInt16(compactHeader, 0x15a, -1);
    WriteInt16(compactHeader, 0x15c, -1);
    WriteInt16(compactHeader, 0x16a, 8);
    WriteInt16(compactHeader, 0x16c, -1);
    var compactSource = new byte[0xa0];
    compactSource.AsSpan(0, 0x20).Fill(0xa1);
    compactSource.AsSpan(0x20, 0x20).Fill(0xa2);
    var preservedInventory = TextureInventoryBuilder.Build([
        new("moby-shared", TextureAssetFamily.Moby, 0x101, 0, TextureRole.Material, sourcePif,
            PreserveReferencedPaletteIndexes: true),
    ]);
    var preservedOptimization = PaletteOptimizer.Optimize(preservedInventory);
    var preservedPif = PaletteTextureWriter.RewritePif(
        sourcePif, preservedOptimization.Assignments.Single(), preservedOptimization.Palettes.Single());
    var rewrittenTexture = PifReader.Read(preservedPif);
    rewrittenTexture.PixelData.CopyTo(compactSource, 0x20);
    rewrittenTexture.MipPixelData[0].CopyTo(compactSource, 0x30);
    for (var index = 0; index < 0x30; index++) compactSource[0x40 + index] = (byte)(0x80 + index);
    compactSource.AsSpan(0x70).Fill(0xa3);
    var meshless = new StaticAssetInput(
        "meshless", TextureAssetFamily.Moby, 0x102, StaticDefinition(TextureAssetFamily.Moby, 0x13),
        ReadOnlyMemory<byte>.Empty, []);
    var compactPalette = new byte[0x1000];
    compactPalette.AsSpan(0, 0x400).Fill(0x5a);
    compactPalette.AsSpan(0xc00, 0x10).Fill(0x6b);
    var compacted = StaticAssetComposer.Compose(
        GameId.UYA, compactHeader, compactSource, compactPalette,
        [inputs[0], inputs[1] with { PreserveTextureIndexes = true }, meshless]);
    var compactedHeader = LevelAssetReader.ReadHeader(compacted.HeaderBytes);
    Expect(compacted.AssetWadBytes.AsSpan(0, 0x40).SequenceEqual(compactSource.AsSpan(0, 0x40)),
        "UYA static composition should retain terrain pixels while replacing old static textures and models");
    var compactedTextures = LevelAssetReader.ReadTextureDefinitions(
        compacted.HeaderBytes, compactedHeader.MobyTextureOffset, compactedHeader.MobyTextureCount);
    var compactedStashedTexture = compactedTextures.Single(value => value.Type == 0);
    var compactedTexture = compactedTextures.Single(value => value.Type != 0);
    Expect(compactedTexture.TextureOffset == 0,
        $"UYA static composition should reuse identical retained terrain pixels (got 0x{compactedTexture.TextureOffset:X})");
    Expect(compactedStashedTexture is { Index: 1, PaletteId: 8, TextureOffset: 0 }
        && LevelAssetReader.ReadModelDefinitions(
                compacted.HeaderBytes, compactedHeader.MobyModelOffset, compactedHeader.MobyModelCount)
            .Single(value => value.ModelId == 0x100).TextureIds[0] == compactedStashedTexture.Index,
        "UYA static composition should retain GS-stashed moby texture definitions and references");
    Expect(compacted.AssetWadBytes.AsSpan(compactedHeader.ParticleTextureDataOffset, 0x30)
            .SequenceEqual(compactSource.AsSpan(0x40, 0x30))
        && compactedHeader.FxTextureDataOffset == compactedHeader.ParticleTextureDataOffset + 0x10
        && compactedHeader.Unused3 == compactedHeader.ParticleTextureDataOffset + 0x20,
        "UYA static composition should preserve and relocate particle, FX, and adjacent opaque data");
    Expect(compactedHeader.SceneViewSize == compacted.AssetWadBytes.Length
        && compactedHeader.DecompressedSize == compacted.AssetWadBytes.Length
        && compactedHeader.CompressedSize == 0,
        "UYA static composition should publish its new decompressed bounds for archive packing");
    Expect(compactedHeader.GsRamCount == 4 && compactedHeader.ExtraMipmapCount == 1,
        $"UYA static composition should retain terrain and stashed GS definitions "
        + $"(got {compactedHeader.GsRamCount} primary and {compactedHeader.ExtraMipmapCount} extra)");
    var compactedMipmaps = LevelAssetReader.ReadMipmapDefinitions(
        compacted.HeaderBytes, compactedHeader.GsRamOffset,
        compactedHeader.GsRamCount + compactedHeader.ExtraMipmapCount);
    var compactedExtra = compactedMipmaps.Last();
    Expect(compacted.PaletteBytes.AsSpan(0, 0x400).SequenceEqual(compactPalette.AsSpan(0, 0x400))
        && compacted.PaletteBytes.AsSpan(compactedExtra.Offset1, 0x10)
            .SequenceEqual(compactPalette.AsSpan(0xc00, 0x10))
        && compactedMipmaps.Take(compactedHeader.GsRamCount)
            .All(value => value.Offset1 < compactedExtra.Offset1),
        "UYA static composition should retain terrain and stashed GS pixels");
    Expect(compactedTexture.PaletteId == 4,
        "UYA static composition should pack rebuilt palettes into gaps around fixed GS destinations");
    Expect(compactedMipmaps.Take(compactedHeader.GsRamCount)
            .Select(value => value.Offset2).SequenceEqual(
                compactedMipmaps.Take(compactedHeader.GsRamCount).Select(value => value.Offset2).Order()),
        "UYA static composition should address-order primary GS RAM records after filling gaps");
    Expect(LevelAssetReader.ReadMobyGsStashClassIds(
            compacted.HeaderBytes, compactedHeader.MobyGsStashListOffset).SequenceEqual([0x100]),
        "UYA static composition should retain the moby GS stash class list");
    Expect(LevelAssetReader.ReadModelDefinitions(
            compacted.HeaderBytes, compactedHeader.MobyModelOffset, compactedHeader.MobyModelCount)
            .Single(value => value.ModelId == 0x102).ModelOffset == 0,
        "UYA static composition should retain intentional meshless moby definitions");

    var sequenceHeader = new byte[0x570];
    compactHeader.CopyTo(sequenceHeader, 0);
    WriteInt32(sequenceHeader, 0x8c, 0xc0);
    WriteInt32(sequenceHeader, 0x78, 0x170);
    WriteInt32(sequenceHeader, 0x170, 0xa0);
    WriteInt32(sequenceHeader, 0x174, 0xb0);
    var sequenceSource = new byte[0xc0];
    compactSource.CopyTo(sequenceSource, 0);
    for (var index = 0; index < 0x20; index++) sequenceSource[0xa0 + index] = (byte)(0x40 + index);
    var sequenceSourceHeader = LevelAssetReader.ReadHeader(sequenceHeader);
    var sequenceSourceMobys = LevelAssetReader.ReadModelDefinitions(
        sequenceHeader, sequenceSourceHeader.MobyModelOffset, sequenceSourceHeader.MobyModelCount);
    var sequenceSourceOffsets = LevelAssetReader.CollectKnownAssetOffsets(
        sequenceSourceHeader, sequenceSource.Length, sequenceSourceMobys, [], [],
        [sequenceSourceHeader.SceneViewSize]);
    Expect(LevelAssetReader.ReadAssetSlice(sequenceSource, 0x70, sequenceSourceOffsets).Length == 0x30,
        "UYA model extraction should stop at the scene-view boundary before Ratchet animations");
    var withSequences = StaticAssetComposer.Compose(
        GameId.UYA, sequenceHeader, sequenceSource, compactPalette, [inputs[0], meshless]);
    var sequenceOutputHeader = LevelAssetReader.ReadHeader(withSequences.HeaderBytes);
    var outputSequenceTable = withSequences.HeaderBytes.AsSpan(^0x400);
    Expect(withSequences.AssetWadBytes.AsSpan(sequenceOutputHeader.SceneViewSize)
            .SequenceEqual(sequenceSource.AsSpan(0xa0))
        && sequenceOutputHeader.DecompressedSize == withSequences.AssetWadBytes.Length,
        "UYA static composition should retain data after the scene-view region");
    Expect(BinaryPrimitives.ReadInt32LittleEndian(outputSequenceTable) == sequenceOutputHeader.SceneViewSize
        && BinaryPrimitives.ReadInt32LittleEndian(outputSequenceTable[4..]) == sequenceOutputHeader.SceneViewSize + 0x10
        && sequenceOutputHeader.LightCuboidsOffset == withSequences.HeaderBytes.Length - 0x400,
        "UYA static composition should relocate and retain the trailing sequence table");

    var mobyTexture = LevelAssetReader.ReadTextureDefinitions(
        composed.HeaderBytes, header.MobyTextureOffset, 1).Single();
    var tieTexture = LevelAssetReader.ReadTextureDefinitions(
        composed.HeaderBytes, header.TieTextureOffset, 1).Single();
    var shrubTexture = LevelAssetReader.ReadTextureDefinitions(
        composed.HeaderBytes, header.ShrubTextureOffset, 1).Single();
    Expect(mobyTexture.PaletteId == tieTexture.PaletteId
        && tieTexture.PaletteId == shrubTexture.PaletteId
        && shrubTexture.PaletteId == shrub.PaletteId
        && mobyTexture.TextureOffset == tieTexture.TextureOffset
        && tieTexture.TextureOffset == shrubTexture.TextureOffset,
        "moby, tie, shrub, and billboard definitions should share identical palette and pixel data");
    Expect(composed.HeaderBytes.Length % 0x10 == 0
        && compacted.HeaderBytes.Length % 0x10 == 0
        && withSequences.HeaderBytes.Length % 0x10 == 0,
        "composed UYA asset headers should retain the runtime's 0x10-byte block alignment");
    foreach (var (name, definition) in new[]
        {
            ("moby", mobyTexture),
            ("tie", tieTexture),
            ("shrub", shrubTexture),
        })
    {
        var outputPif = LevelAssetReader.BuildAssetTexture(
            name, 0, definition, composed.PaletteBytes, composed.AssetWadBytes,
            header.TextureDataOffset, isSwizzled: false).PifBytes;
        Expect(DecodedPifColors(sourcePif).SequenceEqual(DecodedPifColors(outputPif)),
            $"composed {name} texture should preserve every source texel");
    }
    var billboardPif = LevelAssetReader.BuildShrubBillboardTexture(shrub, composed.PaletteBytes).PifBytes;
    Expect(DecodedPifColors(sourcePif).SequenceEqual(DecodedPifColors(billboardPif)),
        "composed shrub billboard should preserve every source texel");
    ValidateUyaCampaignStaticAssetCompositionWhenAvailable();
}

static void ValidateUyaCampaignStaticAssetCompositionWhenAvailable()
{
    var root = Path.Combine("test-assets", "extractions_uya", "level08_iso_world01", "assets");
    var headerPath = Path.Combine(root, "asset_header.bin");
    var assetPath = Path.Combine(root, "asset_wad.bin");
    var palettePath = Path.Combine(root, "palette.bin");
    if (!File.Exists(headerPath) || !File.Exists(assetPath) || !File.Exists(palettePath)) return;

    var sourceHeaderBytes = File.ReadAllBytes(headerPath);
    var sourceAssetBytes = File.ReadAllBytes(assetPath);
    var sourcePaletteBytes = File.ReadAllBytes(palettePath);
    if (BinaryMagic.IsWad(sourceAssetBytes)) sourceAssetBytes = WadCompression.Decompress(sourceAssetBytes);
    var sourceHeader = LevelAssetReader.ReadHeader(sourceHeaderBytes);
    var composed = StaticAssetComposer.Compose(
        GameId.UYA, sourceHeaderBytes, sourceAssetBytes, sourcePaletteBytes, []);
    var header = LevelAssetReader.ReadHeader(composed.HeaderBytes);
    var table = composed.HeaderBytes.AsSpan(header.LightCuboidsOffset, 0x400).ToArray();
    var pointers = Enumerable.Range(0, table.Length / sizeof(int))
        .Select(index => BinaryPrimitives.ReadInt32LittleEndian(table[(index * sizeof(int))..]))
        .Where(value => value != 0)
        .ToArray();
    Expect(sourceHeader.LightCuboidsOffset != header.LightCuboidsOffset
        && header.LightCuboidsOffset == composed.HeaderBytes.Length - table.Length
        && pointers.All(value => value >= header.SceneViewSize
            && value < header.DecompressedSize && value % 0x10 == 0),
        "UYA campaign composition should relocate the Ratchet animation table and every sequence pointer");

    var sourceMipmaps = LevelAssetReader.ReadMipmapDefinitions(
        sourceHeaderBytes, sourceHeader.GsRamOffset,
        sourceHeader.GsRamCount + sourceHeader.ExtraMipmapCount);
    var outputMipmaps = LevelAssetReader.ReadMipmapDefinitions(
        composed.HeaderBytes, header.GsRamOffset,
        header.GsRamCount + header.ExtraMipmapCount);
    var sourceExtra = sourceMipmaps.Skip(sourceHeader.GsRamCount).ToArray();
    var outputExtra = outputMipmaps.Skip(header.GsRamCount).ToArray();
    var gadgetPaletteOffsets = LevelAssetReader.ReadTextureDefinitions(
            sourceHeaderBytes, sourceHeader.MobyTextureOffset, sourceHeader.MobyTextureCount)
        .Where(value => value.Type == 0 && value.PaletteId >= 0)
        .Select(value => value.PaletteId * 0x100)
        .Distinct()
        .ToArray();
    Expect(gadgetPaletteOffsets.All(offset =>
            outputMipmaps.Take(header.GsRamCount)
                .Any(value => value.TextureFormat == 0 && value.Offset2 == offset)
            && composed.PaletteBytes.AsSpan(offset, 0x400)
                .SequenceEqual(sourcePaletteBytes.AsSpan(offset, 0x400))),
        "UYA campaign composition should preserve gadget-WAD moby palettes and GS destinations");
    var sourceStashedTextures = LevelAssetReader.ReadTextureDefinitions(
            sourceHeaderBytes, sourceHeader.MobyTextureOffset, sourceHeader.MobyTextureCount)
        .Where(value => value.Type == 0)
        .Select(value => (value.Index, value.TextureOffset, value.Width, value.Height, value.Type,
            value.PaletteId, value.MipmapPaletteId))
        .ToArray();
    var outputStashedTextures = LevelAssetReader.ReadTextureDefinitions(
            composed.HeaderBytes, header.MobyTextureOffset, header.MobyTextureCount)
        .Where(value => value.Type == 0)
        .Select(value => (value.Index, value.TextureOffset, value.Width, value.Height, value.Type,
            value.PaletteId, value.MipmapPaletteId))
        .ToArray();
    Expect(outputStashedTextures.SequenceEqual(sourceStashedTextures),
        "UYA campaign composition should preserve gadget-WAD moby texture definitions");
    Expect(outputMipmaps.Take(header.GsRamCount)
            .Select(value => value.Offset2).SequenceEqual(
                outputMipmaps.Take(header.GsRamCount).Select(value => value.Offset2).Order()),
        "UYA campaign composition should address-order primary GS RAM records");
    Expect(header.ExtraMipmapCount == sourceHeader.ExtraMipmapCount
        && outputExtra.Select(value => (value.TextureFormat, value.Width, value.Height, value.Offset2))
            .SequenceEqual(sourceExtra.Select(value =>
                (value.TextureFormat, value.Width, value.Height, value.Offset2)))
        && LevelAssetReader.ReadMobyGsStashClassIds(composed.HeaderBytes, header.MobyGsStashListOffset)
            .SequenceEqual(LevelAssetReader.ReadMobyGsStashClassIds(
                sourceHeaderBytes, sourceHeader.MobyGsStashListOffset))
        && header.ChromeTextureOffset == sourceHeader.ChromeTextureOffset
        && header.ChromePaletteOffset == sourceHeader.ChromePaletteOffset
        && header.GlassTextureOffset == sourceHeader.GlassTextureOffset
        && header.GlassPaletteOffset == sourceHeader.GlassPaletteOffset
        && sourceExtra.Zip(outputExtra).All(pair =>
            composed.PaletteBytes.AsSpan(pair.Second.Offset1, pair.Second.Width * pair.Second.Height)
                .SequenceEqual(sourcePaletteBytes.AsSpan(
                    pair.First.Offset1, pair.First.Width * pair.First.Height))),
        "UYA campaign composition should preserve chrome and weapon GS stash definitions and pixels");
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

static void ValidateGameplayPvarTables()
{
    var first = new byte[8];
    WriteInt32(first, 0, 1);
    WriteInt32(first, 4, 4);
    var written = GameplayPvarTableWriter.Write([
        new(first, [0], [4]),
        new([0xaa, 0xbb, 0xcc, 0xdd], [], []),
    ]);
    var reread = GameplayPvarTableReader.Read(PvarBlocks(written), "test")
        ?? throw new InvalidOperationException("Pvar writer produced empty tables.");
    Expect(reread.Entries.Count == 2
        && reread.Entries[0].Data.SequenceEqual(first)
        && reread.Entries[1].Data.SequenceEqual(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd }),
        "Pvar writer should round-trip entry boundaries and opaque bytes");
    Expect(reread.MobyLinks.SequenceEqual([new GameplayPvarRelativePointer(0, 0)]),
        "Pvar writer should round-trip moby fixups");
    Expect(reread.RelativePointers.SequenceEqual([new GameplayPvarRelativePointer(0, 4)]),
        "Pvar writer should round-trip relative-pointer fixups");
    Expect(written.MobyLinksBytes.AsSpan(^8).SequenceEqual(new byte[] {
            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
        }) && written.RelativePointerBytes.AsSpan(^8).SequenceEqual(new byte[] {
            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
        }),
        "Pvar writer should terminate native fixup tables");
    Expect(GameplayPvarTableWriter.Write([
            new(first, [0], [4]),
            new([0xaa, 0xbb, 0xcc, 0xdd], [], []),
        ]).DataBytes.SequenceEqual(written.DataBytes),
        "Pvar writer should be deterministic");

    var malformedFixup = written with { MobyLinksBytes = written.MobyLinksBytes.ToArray() };
    WriteInt32(malformedFixup.MobyLinksBytes, 0, 2);
    ExpectThrows<InvalidDataException>(() => GameplayPvarTableReader.Read(PvarBlocks(malformedFixup), "test"));
    var malformedTable = written with { TableBytes = new byte[7] };
    ExpectThrows<InvalidDataException>(() => GameplayPvarTableReader.Read(PvarBlocks(malformedTable), "test"));

    static GameplayRawBlock[] PvarBlocks(GameplayPvarTables tables) =>
    [
        new(0, 0, 0, "pvar_moby_links", tables.MobyLinksBytes),
        new(1, 0, 0, "pvar_table", tables.TableBytes),
        new(2, 0, 0, "pvar_data", tables.DataBytes),
        new(3, 0, 0, "pvar_relative_pointers", tables.RelativePointerBytes),
    ];
}

static void ValidateGameplayPvarTablesWhenAvailable()
{
    var directory = Path.Combine("test-assets", "extractions_uya");
    if (!Directory.Exists(directory)) return;
    foreach (var path in Directory.EnumerateFiles(directory, "level*.wad", SearchOption.TopDirectoryOnly)
        .Order(StringComparer.Ordinal))
    {
        UyaLevelWadPackage package;
        try
        {
            package = UyaLevelWadUnpacker.Unpack(File.ReadAllBytes(path));
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} has invalid gameplay PVar tables.", exception);
        }
        var gameplay = package.Files.Single(value => value.Path == "gameplay/gameplay_core.bin");
        _ = UyaGameplayBlockReader.ReadCore(gameplay.Bytes);
    }
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
        (0x58, [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]),
        (0x5c, [0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00]),
        (0x60, [0xde, 0xad, 0xbe, 0xef]),
        (0x64, [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]),
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
    Expect(moby.RawBytes.SequenceEqual(mobyBytes.AsSpan(
            UyaMobyInstancesReader.HeaderSize, UyaMobyInstancesReader.RecordSize).ToArray()),
        "UYA moby instance raw bytes should be retained");
    Expect(UyaMobyInstancesWriter.Write(mobyInstances).SequenceEqual(mobyBytes),
        "UYA moby no-edit writes should be byte-identical");
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
    var rebuiltCameraCollision = UyaCameraCollisionGridReader.Read(UyaCameraCollisionGridWriter.Write([
        new(3, 0, 7, 8, 9, new(32, 32, 10), new(0, 0, 0, 1), new(8, 8, 8)),
    ]));
    Expect(rebuiltCameraCollision.OccupiedCellCount == 4,
        "UYA camera collision writer should populate intersecting grid cells");
    Expect(rebuiltCameraCollision.Primitives.Single() is
        { Type: 3, Index: 0, Flags: 7, IntValue: 8, FloatValue: 9, BoundingSphere: { X: 32, Y: 32, Z: 0 } },
        "UYA camera collision writer should preserve metadata and regenerate bounds");
    Expect(MathF.Abs(rebuiltCameraCollision.Primitives.Single().BoundingSphere.W - MathF.Sqrt(192)) < 0.0001f,
        "UYA camera collision writer should derive the transformed radius");

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
    var alwaysVisible = LevelAssetComposer.SetAlwaysVisibleOcclusionBit(
        GameId.UYA, assetHeader, assetWad, 42);
    var alwaysVisibleHeader = LevelAssetReader.ReadHeader(alwaysVisible.HeaderBytes);
    var alwaysVisibleGrid = UyaOcclusionGridReader.ReadLevelAsset(
        alwaysVisible.HeaderBytes, alwaysVisible.AssetWadBytes);
    Expect((alwaysVisible.AssetWadBytes[alwaysVisibleHeader.OcclusionOffset
            + alwaysVisibleGrid.MasksOffset + 42 / 8] & (1 << (42 & 7))) != 0,
        "UYA occlusion composition should make the reserved bit visible in every mask");

    var mappingsBytes = new byte[0x28];
    WriteInt32(mappingsBytes, 4, 1);
    WriteInt32(mappingsBytes, 0x10, 5);
    WriteInt32(mappingsBytes, 0x14, 77);
    var insertedMappings = UyaOcclusionMappingsReader.Read(
        UyaOcclusionMappingsWriter.RemapTies(mappingsBytes, [77, 78], 42)).Ties;
    Expect(insertedMappings.Select(value => (value.BitIndex, value.OcclusionId))
            .SequenceEqual([(5, 77), (42, 78)]),
        "UYA tie insertion should preserve native visibility and map new ties to the reserved bit");

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

static void ValidateUyaMobyInstanceContract()
{
    var bytes = new byte[UyaMobyInstancesReader.HeaderSize + UyaMobyInstancesReader.RecordSize + 3];
    WriteInt32(bytes, 0x00, 1);
    WriteInt32(bytes, 0x04, int.MaxValue);
    WriteInt32(bytes, 0x08, int.MinValue);
    WriteInt32(bytes, 0x0c, int.MaxValue);
    var record = bytes.AsSpan(UyaMobyInstancesReader.HeaderSize, UyaMobyInstancesReader.RecordSize);
    for (var index = 0; index < record.Length; index++) record[index] = unchecked((byte)(index * 37));
    BinaryPrimitives.WriteInt32LittleEndian(record, UyaMobyInstancesReader.RecordSize);
    BinaryPrimitives.WriteSingleLittleEndian(record[0x2c..], float.Epsilon);
    BinaryPrimitives.WriteSingleLittleEndian(record[0x40..], float.MinValue);
    BinaryPrimitives.WriteSingleLittleEndian(record[0x44..], 0);
    BinaryPrimitives.WriteSingleLittleEndian(record[0x48..], float.MaxValue);
    bytes[^3] = 0xaa;
    bytes[^2] = 0xbb;
    bytes[^1] = 0xcc;

    var parsed = UyaMobyInstancesReader.Read(bytes);
    Expect(parsed.Instances.Single().RawBytes.SequenceEqual(record.ToArray())
        && parsed.TrailingBytes.SequenceEqual(new byte[] { 0xaa, 0xbb, 0xcc }),
        "UYA moby boundary records should retain every source byte");
    Expect(UyaMobyInstancesWriter.Write(parsed).SequenceEqual(bytes),
        "UYA moby boundary no-edit writes should be byte-identical");
    Expect(!UyaMobyInstancesReader.TryRead(bytes.AsSpan(0, bytes.Length - 4), out _),
        "UYA moby TryRead should reject truncated records without throwing");

    var impossibleCount = new byte[UyaMobyInstancesReader.HeaderSize];
    WriteInt32(impossibleCount, 0, int.MaxValue);
    Expect(!UyaMobyInstancesReader.TryRead(impossibleCount, out _),
        "UYA moby TryRead should reject record counts larger than the payload");
    var invalidSize = bytes.ToArray();
    WriteInt32(invalidSize, UyaMobyInstancesReader.HeaderSize, UyaMobyInstancesReader.RecordSize - 4);
    ExpectThrows<InvalidDataException>(() => UyaMobyInstancesReader.Read(invalidSize));
    var missingRawBytes = parsed with
    {
        Instances = [parsed.Instances.Single() with { RawBytes = new byte[UyaMobyInstancesReader.RecordSize - 1] }],
    };
    ExpectThrows<InvalidDataException>(() => UyaMobyInstancesWriter.Write(missingRawBytes));
    var invalidRawSize = parsed.Instances.Single().RawBytes.ToArray();
    BinaryPrimitives.WriteInt32LittleEndian(invalidRawSize, UyaMobyInstancesReader.RecordSize - 4);
    ExpectThrows<InvalidDataException>(() => UyaMobyInstancesWriter.Write(parsed with
    {
        Instances = [parsed.Instances.Single() with { RawBytes = invalidRawSize }],
    }));

    ValidateUyaMobyPropertyContract(parsed.Instances.Single().RawBytes, parsed.Instances.Single().ClassId);
}

static void ValidateUyaMobyPropertyContract(byte[] source, int classId)
{
    var cases = new (UyaMobyInstanceField Field, UyaMobyInstanceFieldValue Value, int Offset, int Length)[]
    {
        (UyaMobyInstanceField.Mission, UyaMobyInstanceFieldValue.FromInteger(-1), 0x04, 4),
        (UyaMobyInstanceField.Mission, UyaMobyInstanceFieldValue.FromInteger(sbyte.MaxValue), 0x04, 4),
        (UyaMobyInstanceField.Bolts, UyaMobyInstanceFieldValue.FromInteger(0), 0x14, 4),
        (UyaMobyInstanceField.Bolts, UyaMobyInstanceFieldValue.FromInteger(int.MaxValue), 0x14, 4),
        (UyaMobyInstanceField.DrawDistance, UyaMobyInstanceFieldValue.FromInteger(0), 0x30, 4),
        (UyaMobyInstanceField.UpdateDistance, UyaMobyInstanceFieldValue.FromInteger(int.MaxValue), 0x34, 4),
        (UyaMobyInstanceField.IsRooted, UyaMobyInstanceFieldValue.FromBoolean(false), 0x5c, 4),
        (UyaMobyInstanceField.IsRooted, UyaMobyInstanceFieldValue.FromBoolean(true), 0x5c, 4),
        (UyaMobyInstanceField.RootedDistance, UyaMobyInstanceFieldValue.FromFloat(-1), 0x60, 4),
        (UyaMobyInstanceField.RootedDistance, UyaMobyInstanceFieldValue.FromFloat(float.MaxValue), 0x60, 4),
        (UyaMobyInstanceField.Color, UyaMobyInstanceFieldValue.FromColor(new(0, 127, 255)), 0x74, 12),
    };
    foreach (var test in cases)
    {
        var edited = UyaMobyInstancesWriter.WriteField(source, classId, new(test.Field, test.Value));
        var reread = UyaMobyInstancesReader.ReadInstance(edited);
        Expect(edited.Where((value, index) => index < test.Offset || index >= test.Offset + test.Length)
                .SequenceEqual(source.Where((value, index) => index < test.Offset || index >= test.Offset + test.Length)),
            $"UYA moby field {test.Field} should preserve every non-owned byte");
        Expect(ReadUyaMobyField(reread, test.Field) == test.Value,
            $"UYA moby field {test.Field} should round-trip its typed value");
    }

    ExpectThrows<ArgumentOutOfRangeException>(() => UyaMobyInstancesWriter.WriteField(
        source, classId, new((UyaMobyInstanceField)(-1), UyaMobyInstanceFieldValue.FromInteger(1))));
    ExpectThrows<ArgumentOutOfRangeException>(() => UyaMobyInstancesWriter.WriteField(
        source, classId, new(UyaMobyInstanceField.Bolts, UyaMobyInstanceFieldValue.FromBoolean(true))));
    ExpectThrows<ArgumentOutOfRangeException>(() => UyaMobyInstancesWriter.WriteField(
        source, classId, new(UyaMobyInstanceField.Mission, UyaMobyInstanceFieldValue.FromInteger(128))));
    ExpectThrows<ArgumentOutOfRangeException>(() => UyaMobyInstancesWriter.WriteField(
        source, classId, new(UyaMobyInstanceField.RootedDistance,
            UyaMobyInstanceFieldValue.FromFloat(float.NaN))));
    ExpectThrows<ArgumentOutOfRangeException>(() => UyaMobyInstancesWriter.WriteField(
        source, classId, new(UyaMobyInstanceField.Color,
            UyaMobyInstanceFieldValue.FromColor(new(0, 0, 256)))));
    ExpectThrows<InvalidDataException>(() => UyaMobyInstancesWriter.WriteField(
        source, classId + 1, new(UyaMobyInstanceField.Bolts,
            UyaMobyInstanceFieldValue.FromInteger(1))));
}

static UyaMobyInstanceFieldValue ReadUyaMobyField(UyaMobyInstance instance, UyaMobyInstanceField field) => field switch
{
    UyaMobyInstanceField.Mission => UyaMobyInstanceFieldValue.FromInteger(instance.Mission),
    UyaMobyInstanceField.Bolts => UyaMobyInstanceFieldValue.FromInteger(instance.Bolts),
    UyaMobyInstanceField.DrawDistance => UyaMobyInstanceFieldValue.FromInteger(instance.DrawDistance),
    UyaMobyInstanceField.UpdateDistance => UyaMobyInstanceFieldValue.FromInteger(instance.UpdateDistance),
    UyaMobyInstanceField.IsRooted => UyaMobyInstanceFieldValue.FromBoolean(instance.IsRooted != 0),
    UyaMobyInstanceField.RootedDistance => UyaMobyInstanceFieldValue.FromFloat(instance.RootedDistance),
    UyaMobyInstanceField.Color => UyaMobyInstanceFieldValue.FromColor(instance.Color),
    _ => throw new ArgumentOutOfRangeException(nameof(field)),
};

static void ValidateUyaStaticInstanceParsing()
{
    var tieTemplate = UyaGameplayInstanceTemplates.CreateTie(0x1200, 7);
    var placedTie = UyaTieInstancesReader.ReadInstance(tieTemplate);
    Expect(placedTie is { ClassId: 0x1200, OcclusionId: 7 }
        && BinaryPrimitives.ReadInt32LittleEndian(tieTemplate.AsSpan(4))
            == UyaGameplayInstanceTemplates.DefaultTieDrawDistance,
        "UYA tie templates should expose their semantic identifier and use the retail draw-distance encoding");
    WriteSingle(tieTemplate, 4, UyaGameplayInstanceTemplates.DefaultTieDrawDistance);
    var normalizedTie = UyaGameplayInstanceTemplates.NormalizePlacedTie(tieTemplate);
    Expect(BinaryPrimitives.ReadInt32LittleEndian(normalizedTie.AsSpan(4))
            == UyaGameplayInstanceTemplates.DefaultTieDrawDistance,
        "UYA tie template normalization should repair the legacy float draw-distance encoding");

    var shrubTemplate = UyaGameplayInstanceTemplates.CreateShrub(0x1300);
    Expect(BinaryPrimitives.ReadSingleLittleEndian(shrubTemplate.AsSpan(4))
            == UyaGameplayInstanceTemplates.DefaultShrubDrawDistance
        && BinaryPrimitives.ReadInt32LittleEndian(shrubTemplate.AsSpan(0x50)) == 255,
        "UYA shrub templates should contain safe draw-distance and neutral-light defaults");
    var mobyTemplate = UyaGameplayInstanceTemplates.CreateMoby(0x1400, 9);
    Expect(UyaMobyInstancesReader.ReadInstance(mobyTemplate) is { ClassId: 0x1400, Uid: 9 },
        "UYA moby templates should expose their assigned UID");
    Expect(UyaGameplayInstanceTemplates.CreateNeutralTieAmbient(4)
            .SequenceEqual(new byte[] { 0x80, 0x80, 0x80, 0 }),
        "UYA neutral tie ambient data should retain its retail word layout");

    var definitionA = Enumerable.Range(0, 0x20).Select(value => (byte)value).ToArray();
    var definitionB = definitionA.ToArray();
    definitionB.AsSpan(0, 8).Fill(0xaa);
    definitionB.AsSpan(0x10).Fill(0xbb);
    Expect(UyaLevelAssetCanonicalizer.NormalizeDefinition(definitionA)
            .SequenceEqual(UyaLevelAssetCanonicalizer.NormalizeDefinition(definitionB)),
        "UYA asset definition canonicalization should ignore placement-specific fields");

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
    var classIds = UyaClassIdListWriter.Write([0x300, 0x100, 0x300, 0x200]);
    Expect(classIds.Length == 0x10
        && BinaryPrimitives.ReadInt32LittleEndian(classIds) == 3
        && Enumerable.Range(0, 3).Select(index =>
                BinaryPrimitives.ReadInt32LittleEndian(classIds.AsSpan(4 + index * 4)))
            .SequenceEqual(new[] { 0x100, 0x200, 0x300 }),
        "UYA class list writer should deduplicate, sort, and align class IDs");

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
    grindPathBytes[0x2c] = 0x7f;
    var rebuiltGrindPath = GameplayGeometryReader.ReadGrindPaths(UyaGrindPathInstancesWriter.Write(
        grindPathBytes,
        [new(0,
            [new(1, 2, 3, 9), new(5, 6, 7, 10), new(9, 10, 11, 12)],
            new(10, 20, 30), new(0, 0, 0, 1), new(2, 3, 4))])).Single();
    Expect(rebuiltGrindPath.Points[2] == new GameplayVector4(28, 50, 74, 12)
        && rebuiltGrindPath.BoundingSphere.X == 20 && rebuiltGrindPath.BoundingSphere.Y == 38
        && rebuiltGrindPath.BoundingSphere.Z == 58
        && rebuiltGrindPath.Unknown4 == 11 && rebuiltGrindPath.Wrap == 1 && rebuiltGrindPath.Inactive == 2,
        "UYA grind-path writer should rebuild points and bounds while preserving metadata");
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
    var editedDirectional = UyaGameplayLightingReader.ReadDirectionalLights(
        UyaDirectionalLightsWriter.Write(directional, [new(0, new(0, 0, 1, 0))])).Single();
    Expect(editedDirectional.InverseDirection.X == -0.75f
        && editedDirectional.TopDirection.W == -1 && editedDirectional.TopColor.X == 0.25f,
        "UYA directional-light writer should rotate directions and preserve other fields");
    Expect(lighting.PointLights.MasksMatchDerived
        && lighting.PointLights.Lights.Single().Position == new GameplayVector3(32, 16, 8)
        && lighting.PointLights.Lights.Single().ColorR == 0xffff,
        "UYA point lights and masks should be parsed");
    var editedPointLights = UyaGameplayLightingReader.ReadPointLights(UyaPointLightsWriter.Write(
        point, [new(0, new(48, 24, 12), 10)]));
    Expect(editedPointLights.MasksMatchDerived
        && editedPointLights.Lights.Single().Position == new GameplayVector3(48, 24, 12)
        && editedPointLights.Lights.Single().Radius == 10
        && editedPointLights.Lights.Single().ColorR == 0xffff,
        "UYA point-light writer should patch transforms and rebuild masks");
    Expect(lighting.EnvironmentSamplePoints.Single().Position == new GameplayVector3(10, 20, 30)
        && lighting.EnvironmentSamplePoints.Single().FogColor == new UyaRgb24(40, 50, 60),
        "UYA environment sample points should be parsed");
    var editedSample = UyaGameplayLightingReader.ReadEnvironmentSamplePoints(
        UyaEnvironmentSamplePointsWriter.Write(sample, [new(0, new(-1.25f, 2.5f, 3.75f))])).Single();
    Expect(editedSample.Position == new GameplayVector3(-1.25f, 2.5f, 3.75f)
        && editedSample.HeroLight == 3 && editedSample.FogColor == new UyaRgb24(40, 50, 60),
        "UYA environment-sample writer should patch only position");
    Expect(lighting.EnvironmentTransitions.Single().BoundingSphere.W == 4
        && lighting.EnvironmentTransitions.Single().Flags == 3
        && lighting.EnvironmentTransitions.Single().FogFarIntensity2 == 80,
        "UYA environment transitions should be parsed");
    var editedTransition = UyaGameplayLightingReader.ReadEnvironmentTransitions(
        UyaEnvironmentTransitionsWriter.Write(transition,
            [new(0, new(4, 5, 6), new(0, 0, 0, 1), new(2, 3, 4))])).Single();
    Expect(editedTransition.BoundingSphere == new GameplayVector4(4, 5, 6, MathF.Sqrt(29))
        && editedTransition.InverseMatrix[0] == 0.5f
        && editedTransition.Flags == 3 && editedTransition.FogFarIntensity2 == 80,
        "UYA environment-transition writer should patch transform and derived bounds");
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

static void ValidateUyaCollisionParsingAndGltf()
{
    var bytes = CreateSyntheticUyaCollision();
    var collision = UyaCollisionReader.Read(bytes);

    Expect(collision.NativeOctantCount == 2, "UYA collision should report native octants");
    Expect(collision.NativeFaceCount == 3, "UYA collision should report native faces");
    Expect(collision.DuplicateSolidFaceCount == 1, "UYA collision should deduplicate octant faces");
    Expect(collision.SolidPieces.Count == 2, "UYA collision should split disconnected solid pieces");
    var quad = collision.SolidPieces[0].Faces.Single();
    var triangle = collision.SolidPieces[1].Faces.Single();
    Expect(quad.Type == 0x21 && quad.IsQuad, "UYA collision should preserve solid type nibbles and quads");
    Expect(quad.A == new UyaCollisionVertex(64, 64, 128), "UYA collision should convert octant-local coordinates to world ticks");
    Expect(triangle.Type == 0xab && !triangle.IsQuad, "UYA collision should preserve sound and collision nibbles and triangles");
    var barrier = collision.PlayerBarriers.Single();
    Expect(barrier.Triangles.Single() == new UyaCollisionTriangle(0, 1, 2), "UYA collision should parse player barriers");
    Expect(barrier.BoundingSphere.Value == new Vector4(1, 1, 1, 2), "UYA collision should preserve player barrier bounds");
    var inspection = CollisionConverter.Inspect(bytes, GameId.UYA);
    Expect(inspection.Pieces.Count == 3
        && inspection.Pieces[1].Types.Single() == new CollisionTypeCount(0xab, 1),
        "collision inspection should expose stable pieces and raw type counts");
    var analysis = CollisionConverter.Analyze(bytes, GameId.UYA);
    Expect(analysis.LogicalFaceCount == 2
        && analysis.OccupiedOctantCount == 2
        && analysis.DuplicateFaceCount == 1
        && analysis.Octants.Sum(octant => octant.EncodedByteCount) == 80,
        "collision analysis should expose exact native octant costs");
    Expect(analysis.Octants.All(octant => octant.AdditionIds.Count == 0),
        "source collision analysis should not invent addition ownership");

    var noOp = CollisionConverter.Compose(bytes, GameId.UYA, []);
    Expect(!noOp.Changed && ReferenceEquals(noOp.Bytes, bytes), "collision no-op should preserve source bytes");
    var edits = new[]
    {
        new CollisionPieceEdit(CollisionPieceKind.Solid, 0, 1, 0, 0),
        new CollisionPieceEdit(CollisionPieceKind.Solid, 1, 0, 0, 0, Remove: true),
        new CollisionPieceEdit(CollisionPieceKind.PlayerBarrier, 0, 1, 1, 0),
    };
    var composed = CollisionConverter.Compose(bytes, GameId.UYA, edits);
    var rebuilt = UyaCollisionReader.Read(composed.Bytes);
    Expect(composed.Changed
        && rebuilt.SolidPieces.Count == 1
        && rebuilt.SolidPieces[0].Faces.Single().A == new UyaCollisionVertex(128, 64, 128)
        && rebuilt.SolidPieces[0].Faces.Single().Type == 0x21,
        "collision composition should translate and remove solid pieces without changing raw types");
    Expect(rebuilt.PlayerBarriers.Single().Vertices[0] == new UyaCollisionVertex(128, 128, 64),
        "collision composition should translate player barriers");
    var repeatedComposition = CollisionConverter.Compose(bytes, GameId.UYA, edits);
    Expect(composed.Bytes.SequenceEqual(repeatedComposition.Bytes), "collision composition should be deterministic");
    var addition = new CollisionSolidAddition("tie:crate",
    [
        new(
            0x31,
            new(11, 10, 10),
            new(10, 11, 10),
            new(10, 10, 10),
            default,
        IsQuad: false),
    ]);
    var transformedAddition = CollisionWork.TransformAddition(
        addition,
        GameId.UYA,
        "entity:rotated",
        new(
            new(1, 2, 3),
            new(0, 0, MathF.Sin(MathF.PI / 4), MathF.Cos(MathF.PI / 4)),
            new(2, 1, 1)));
    Expect(transformedAddition.Id == "entity:rotated"
        && transformedAddition.Faces.Single().A == new CollisionVertex(-9, 24, 13)
        && transformedAddition.Faces.Single().B == new CollisionVertex(-10, 22, 13)
        && transformedAddition.Faces.Single().C == new CollisionVertex(-9, 22, 13),
        "collision addition transforms should apply scale, rotation, translation, and target quantization");
    var mirroredAddition = CollisionWork.TransformAddition(
        addition,
        GameId.UYA,
        "entity:mirrored",
        new(new(20, 0, 0), new(0, 0, 0, 1), new(-1, 1, 1)));
    Expect(mirroredAddition.Faces.Single().A == new CollisionVertex(9, 10, 10)
        && mirroredAddition.Faces.Single().B == new CollisionVertex(10, 10, 10)
        && mirroredAddition.Faces.Single().C == new CollisionVertex(10, 11, 10),
        "mirrored collision addition transforms should reverse winding");
    var relativeAddition = CollisionWork.TransformAdditionRelative(
        addition,
        GameId.UYA,
        "entity:relative",
        new(new(0, 0, 0), new(0, 0, 0, 1), new(1, 1, 1)),
        new(new(10, 0, 0), new(0, 0, 0, 1), new(1, 1, 1)),
        new(new(20, 0, 0), new(0, 0, 0, 1), new(1, 1, 1)));
    Expect(relativeAddition.Faces.Single().A == new CollisionVertex(21, 10, 10),
        "relative collision transforms should follow the parent transform delta");
    var fineAddition = new CollisionSolidAddition("tie:fine",
    [
        new(0x31, new(-10 / 16f, -10 / 16f, 0), new(-10 / 16f, -9 / 16f, 0),
            new(-9 / 16f, -10 / 16f, 0), default, IsQuad: false),
        new(0x31, new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), default, IsQuad: false),
    ]);
    var quantizedAddition = CollisionWork.TransformAddition(
        fineAddition,
        GameId.UYA,
        "entity:scaled-fine",
        new(new(333.4231f, 337.6416f, 66.07914f), new(0, 0, 0, 1),
            new(0.56691813f, 0.56691813f, 0.56691813f)));
    Expect(quantizedAddition.Faces.Count == 1
        && CollisionWork.EncodeStandalone(GameId.UYA, [quantizedAddition]).Analysis?.HardViolationCount == 0,
        "instance quantization should discard fine faces that collapse at the placed TIE scale");
    var withAddition = CollisionConverter.Compose(bytes, GameId.UYA, [], [addition]);
    var standalone = CollisionConverter.EncodeStandalone(GameId.UYA, [addition]);
    var decodedStandalone = CollisionWork.DecodeSolidAddition(
        standalone.Bytes, GameId.UYA, "decoded:proxy");
    var standaloneInspection = CollisionConverter.Inspect(standalone.Bytes, GameId.UYA);
    Expect(standaloneInspection.Pieces is [{ Kind: CollisionPieceKind.Solid, FaceCount: 1 }]
        && standalone.Analysis is { LogicalFaceCount: 1, HardViolationCount: 0 }
        && decodedStandalone is { Id: "decoded:proxy", Faces.Count: 1 }
        && decodedStandalone.Faces.Single().RawType == 0x31,
        "standalone collision encoding should write and verify SDK additions");
    var decodedPiece = CollisionWork.DecodeSolidPieceAddition(
        standalone.Bytes, GameId.UYA, 0, "decoded:piece");
    var decodedPieces = CollisionWork.DecodeSolidPieces(standalone.Bytes, GameId.UYA);
    Expect(decodedPiece.Faces.SequenceEqual(decodedStandalone.Faces)
        && decodedPieces is [{ SourcePieceIndex: 0 }]
        && decodedPieces[0].Faces.SequenceEqual(decodedStandalone.Faces),
        "single and bulk collision piece extraction should retain exact authored faces");
    var addedFace = UyaCollisionReader.Read(withAddition.Bytes).SolidPieces
        .SelectMany(piece => piece.Faces)
        .Single(face => face.Type == 0x31);
    Expect(addedFace.A == new UyaCollisionVertex(640, 640, 640),
        "collision composition should add quantized SDK solid faces");
    var additionAnalysis = withAddition.Analysis
        ?? throw new InvalidOperationException("collision addition composition did not return analysis");
    Expect(additionAnalysis is
        {
            LogicalFaceCount: 3,
            OccupiedOctantCount: 3,
            DuplicateFaceCount: 1,
        } && additionAnalysis.Octants.Single(octant => octant.AdditionIds.Count > 0)
            .AdditionIds.SequenceEqual(["tie:crate"]),
        "collision composition should report the responsible addition for each affected octant");
    var analyzedAddition = CollisionConverter.Analyze(withAddition.Bytes, GameId.UYA);
    Expect(additionAnalysis.Octants.Select(octant => (
            octant.X,
            octant.Y,
            octant.Z,
            octant.FaceCount,
            octant.VertexCount,
            octant.QuadCount,
            octant.EncodedByteCount)).SequenceEqual(analyzedAddition.Octants.Select(octant => (
            octant.X,
            octant.Y,
            octant.Z,
            octant.FaceCount,
            octant.VertexCount,
            octant.QuadCount,
            octant.EncodedByteCount))),
        "collision composition analysis should match analysis of the encoded output");
    var repeatedAddition = CollisionConverter.Compose(bytes, GameId.UYA, [], [addition]);
    Expect(withAddition.Bytes.SequenceEqual(repeatedAddition.Bytes),
        "collision additions should compose deterministically");
    var boundaryAnalysis = CollisionConverter.AnalyzeComposition(bytes, GameId.UYA, [],
    [
        new("octant-boundary",
        [
            new(0x31,
                new(3.875f, 10.125f, 10.125f),
                new(4.125f, 10.125f, 10.125f),
                new(3.875f, 10.375f, 10.125f),
                default,
                IsQuad: false),
        ]),
    ]);
    var boundaryOctants = boundaryAnalysis.Octants
        .Where(octant => octant.AdditionIds.Contains("octant-boundary"))
        .ToArray();
    Expect(boundaryOctants.Length == 2
        && boundaryOctants.Select(octant => octant.X).Order().SequenceEqual([0, 1]),
        "collision analysis should charge a proxy face to both sides of an octant boundary");
    var boundaryTouchAnalysis = CollisionConverter.AnalyzeComposition(bytes, GameId.UYA, [],
    [
        new("starts-on-boundary",
        [
            new(0x31, new(4, 30, 30), new(4.25f, 30, 30), new(4, 30.25f, 30),
                default, IsQuad: false),
        ]),
        new("ends-on-boundary",
        [
            new(0x31, new(3.75f, 34, 30), new(4, 34, 30), new(4, 34.25f, 30),
                default, IsQuad: false),
        ]),
        new("lies-on-boundary",
        [
            new(0x31, new(4, 38, 30), new(4, 38.25f, 30), new(4, 38, 30.25f),
                default, IsQuad: false),
        ]),
    ]);
    Expect(boundaryTouchAnalysis.Octants
            .Where(octant => octant.AdditionIds.Contains("starts-on-boundary"))
            .Select(octant => octant.X).Distinct().SequenceEqual([1])
        && boundaryTouchAnalysis.Octants
            .Where(octant => octant.AdditionIds.Contains("ends-on-boundary"))
            .Select(octant => octant.X).Distinct().SequenceEqual([0])
        && boundaryTouchAnalysis.Octants
            .Where(octant => octant.AdditionIds.Contains("lies-on-boundary"))
            .Select(octant => octant.X).Distinct().SequenceEqual([1]),
        "collision analysis should use half-open octants for boundary-touching faces");
    var wideFace = new UyaCollisionSolidFace(
        0x31,
        new(0, 40 * 64, 40 * 64),
        new(60 * 64, 40 * 64, 40 * 64),
        new(0, 41 * 64, 40 * 64),
        default,
        IsQuad: false);
    var wideCollision = new UyaMapCollision([new(0, [wideFace])], [], 0, 0, 0);
    var wideBytes = UyaCollisionWriter.Write(wideCollision);
    Expect(UyaCollisionWriter.Analyze(wideCollision) is { HardViolationCount: 0, OccupiedOctantCount: 1 }
        && CollisionConverter.Inspect(wideBytes, GameId.UYA).Pieces.Single().FaceCount == 1,
        "collision encoding should only index a wide face in octants that can pack every vertex");
    ExpectThrows<ArgumentException>(() => CollisionConverter.Compose(bytes, GameId.UYA, [],
    [
        addition,
        addition,
    ]));
    ExpectThrows<InvalidDataException>(() => CollisionConverter.Compose(bytes, GameId.UYA, [],
    [
        new("duplicate-source",
        [
            new(
                quad.Type,
                new(quad.A.Position.X, quad.A.Position.Y, quad.A.Position.Z),
                new(quad.B.Position.X, quad.B.Position.Y, quad.B.Position.Z),
                new(quad.C.Position.X, quad.C.Position.Y, quad.C.Position.Z),
                new(quad.D.Position.X, quad.D.Position.Y, quad.D.Position.Z),
                quad.IsQuad),
        ]),
    ]));
    ExpectThrows<ArgumentException>(() => CollisionConverter.Compose(bytes, GameId.UYA, [],
    [
        new("empty", []),
    ]));
    ExpectThrows<ArgumentException>(() => CollisionConverter.Compose(bytes, GameId.UYA, [],
    [
        new("degenerate",
        [
            new(0x31, new(10, 10, 10), new(10, 10, 10), new(10, 11, 10), default, IsQuad: false),
        ]),
    ]));
    ExpectThrows<ArgumentOutOfRangeException>(() => CollisionConverter.Compose(bytes, GameId.UYA, [],
    [
        new("non-finite",
        [
            new(0x31, new(float.NaN, 10, 10), new(11, 10, 10), new(10, 11, 10), default, IsQuad: false),
        ]),
    ]));
    var excessiveFaces = Enumerable.Range(0, 86).Select(index =>
    {
        var x = 20.125f + index % 10 * 0.25f;
        var y = 20.125f + index / 10 * 0.25f;
        return new CollisionSolidFace(
            0x31,
            new(x, y, 20.125f),
            new(x + 0.0625f, y, 20.125f),
            new(x, y + 0.0625f, 20.125f),
            default,
            IsQuad: false);
    }).ToArray();
    var excessiveAnalysis = CollisionConverter.AnalyzeComposition(bytes, GameId.UYA, [],
    [
        new("too-dense", excessiveFaces),
    ]);
    Expect(excessiveAnalysis.HardViolationCount > 0
        && excessiveAnalysis.Octants.Any(octant => octant.Violations.Count > 0
            && octant.AdditionIds.Contains("too-dense")),
        "collision composition analysis should identify unsafe additions before writing");
    ExpectThrows<InvalidDataException>(() => CollisionConverter.Compose(bytes, GameId.UYA, [],
    [
        new("too-dense", excessiveFaces),
    ]));
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        ExpectThrows<OperationCanceledException>(() => UyaCollisionComposer.Compose(bytes, [],
        [
            new("cancelled",
            [
                new(0x31,
                    new(640, 640, 640),
                    new(704, 640, 640),
                    new(640, 704, 640),
                    default,
                    IsQuad: false),
            ]),
        ], cancelled.Token));
    }
    var removed = CollisionConverter.Compose(bytes, GameId.UYA,
    [
        new(CollisionPieceKind.Solid, 0, 0, 0, 0, Remove: true),
        new(CollisionPieceKind.Solid, 1, 0, 0, 0, Remove: true),
        new(CollisionPieceKind.PlayerBarrier, 0, 0, 0, 0, Remove: true),
    ]);
    var emptyCollision = UyaCollisionReader.Read(removed.Bytes);
    Expect(emptyCollision.SolidPieces.Count == 0 && emptyCollision.PlayerBarriers.Count == 0,
        "collision composition should support deleting every piece");
    ExpectThrows<ArgumentException>(() => UyaCollisionComposer.Compose(bytes,
    [
        new(UyaCollisionPieceKind.Solid, 0, 1, 0, 0),
    ]));
    ExpectThrows<ArgumentException>(() => CollisionConverter.Compose(bytes, GameId.UYA,
    [
        new(CollisionPieceKind.Solid, 0, 0, 0, 0, Remove: true),
        new(CollisionPieceKind.Solid, 0, 1, 0, 0),
    ]));
    ExpectThrows<NotSupportedException>(() => CollisionConverter.Compose(bytes, GameId.DL, []));
    ExpectThrows<NotSupportedException>(() => CollisionConverter.Analyze(bytes, GameId.DL));

    var files = CollisionConverter.ExportGltf(bytes, GameId.UYA, "collision.gltf", minify: true);
    using var gltf = JsonDocument.Parse(files.GltfBytes);
    var root = gltf.RootElement;
    var nodes = root.GetProperty("nodes").EnumerateArray().ToArray();
    Expect(nodes.Any(node => node.GetProperty("name").GetString() == "solid_collision_0000"), "collision glTF should name solid pieces");
    Expect(nodes.Any(node => node.GetProperty("name").GetString() == "player_barrier_0000"), "collision glTF should name player barriers");
    var typedNode = nodes.Single(node => node.GetProperty("name").GetString() == "solid_collision_0001");
    Expect(typedNode.GetProperty("extras").GetProperty("rawTypeHistogram").GetProperty("0xAB").GetInt32() == 1, "collision glTF should expose raw type IDs");
    Expect(typedNode.GetProperty("extras").GetProperty("collisionTypeHistogram").GetProperty("0xB").GetInt32() == 1, "collision glTF should expose collision nibbles");
    Expect(typedNode.GetProperty("extras").GetProperty("soundTypeHistogram").GetProperty("0xA").GetInt32() == 1, "collision glTF should expose sound nibbles");
    var materials = root.GetProperty("materials").EnumerateArray().ToArray();
    Expect(materials.Length == 2, "collision glTF should contain solid and barrier materials");
    Expect(materials[1].GetProperty("alphaMode").GetString() == "BLEND", "player barriers should be translucent");
    Expect(files.BinBytes.Length > 0, "collision glTF should include geometry data");
    var meshPrimitives = root.GetProperty("meshes").EnumerateArray()
        .Select(mesh => mesh.GetProperty("primitives")[0]).ToArray();
    var quadFaceIds = ReadAccessorUInt32Values(
        root,
        files.BinBytes,
        meshPrimitives[0].GetProperty("attributes").GetProperty("_COLLISION_FACE_ID").GetInt32());
    var triangleFaceIds = ReadAccessorUInt32Values(
        root,
        files.BinBytes,
        meshPrimitives[1].GetProperty("attributes").GetProperty("_COLLISION_FACE_ID").GetInt32());
    Expect(quadFaceIds.SequenceEqual([0u, 0u, 0u, 0u])
        && triangleFaceIds.SequenceEqual([1u, 1u, 1u]),
        "collision glTF should expose one deterministic global ID per native solid face");
    Expect(!meshPrimitives[2].GetProperty("attributes").TryGetProperty("_COLLISION_FACE_ID", out _),
        "player barriers should not expose editable solid face IDs");
    var repeated = CollisionConverter.ExportGltf(bytes, GameId.UYA, "collision.gltf", minify: true);
    Expect(files.GltfBytes.SequenceEqual(repeated.GltfBytes) && files.BinBytes.SequenceEqual(repeated.BinBytes), "collision glTF should be deterministic");

    var generic = UyaCollisionGltfPalette.Generic;
    var collisionColors = Enumerable.Repeat(Vector3.Zero, 16).ToArray();
    var soundColors = Enumerable.Repeat(Vector3.Zero, 16).ToArray();
    collisionColors[0xb] = Vector3.UnitX;
    soundColors[0xa] = Vector3.UnitZ;
    var customPalette = new UyaCollisionGltfPalette(collisionColors, soundColors, Vector3.One);
    var custom = UyaCollisionGltfExporter.Export(
        collision,
        options: new UyaCollisionGltfExportOptions { Palette = customPalette, Minify = true });
    var sdkCustom = CollisionConverter.ExportGltf(
        bytes,
        GameId.UYA,
        minify: true,
        palette: new(collisionColors, soundColors, Vector3.One));
    Expect(sdkCustom.GltfBytes.SequenceEqual(custom.GltfBytes)
        && sdkCustom.BinBytes.SequenceEqual(custom.BinBytes),
        "collision SDK facade should forward palette overrides");
    Expect(!files.BinBytes.SequenceEqual(custom.BinBytes), "collision glTF should honor palette overrides");
    using var customGltf = JsonDocument.Parse(custom.GltfBytes);
    var customRoot = customGltf.RootElement;
    var colorAccessorIndex = customRoot.GetProperty("meshes")[1]
        .GetProperty("primitives")[0]
        .GetProperty("attributes")
        .GetProperty("COLOR_0")
        .GetInt32();
    var colorViewIndex = customRoot.GetProperty("accessors")[colorAccessorIndex]
        .GetProperty("bufferView")
        .GetInt32();
    var colorOffset = customRoot.GetProperty("bufferViews")[colorViewIndex]
        .GetProperty("byteOffset")
        .GetInt32();
    Expect(
        custom.BinBytes.AsSpan(colorOffset, 4).SequenceEqual(new byte[] { 255, 0, 0, 255 }),
        "collision glTF should use the low collision nibble as its base color");
    var customAttributes = customRoot.GetProperty("meshes")[1]
        .GetProperty("primitives")[0]
        .GetProperty("attributes");
    Expect(ReadFirstAccessorByte(customRoot, custom.BinBytes, customAttributes.GetProperty("_COLLISION_TYPE").GetInt32()) == 0xb
        && ReadFirstAccessorByte(customRoot, custom.BinBytes, customAttributes.GetProperty("_SOUND_TYPE").GetInt32()) == 0xa,
        "collision glTF should preserve collision and sound nibbles as vertex attributes");

    var compressed = WadCompression.Compress(bytes);
    var collisionEnd = (0x10 + compressed.Length + 0x0f) & ~0x0f;
    var chunk = new byte[collisionEnd + 0x20];
    WriteInt32(chunk, 4, 0x10);
    WriteInt32(chunk, 8, collisionEnd);
    compressed.CopyTo(chunk.AsSpan(0x10));
    chunk.AsSpan(collisionEnd, 0x20).Fill(0x5c);
    Expect(UyaCollisionReader.ReadChunkWad(chunk).NativeFaceCount == 3, "UYA chunk WAD collision should be decompressed");
    var composedChunk = LevelAssetComposer.ComposeTfragChunkCollision(GameId.UYA, chunk, composed.Bytes);
    var movedCollisionSuffix = BinaryPrimitives.ReadInt32LittleEndian(composedChunk.AsSpan(8));
    Expect(TfragChunkWadReader.ReadCollisionPayload(composedChunk).SequenceEqual(composed.Bytes)
        && composedChunk.AsSpan(movedCollisionSuffix, 0x20).SequenceEqual(chunk.AsSpan(collisionEnd, 0x20)),
        "UYA chunk composer should replace compressed collision and preserve later payloads");
    var unchangedChunk = LevelAssetComposer.ComposeTfragChunkCollision(GameId.UYA, chunk, bytes);
    Expect(unchangedChunk.SequenceEqual(chunk), "UYA chunk collision no-op should remain byte-identical");
    var badCompression = new byte[0x20];
    WriteInt32(badCompression, 4, 0x10);
    badCompression[0x10] = (byte)'W';
    badCompression[0x11] = (byte)'A';
    badCompression[0x12] = (byte)'D';
    WriteInt32(badCompression, 0x13, int.MaxValue);
    ExpectThrows<InvalidDataException>(() => UyaCollisionReader.ReadChunkWad(badCompression));

    var badIndex = (byte[])bytes.Clone();
    badIndex[0x84] = 7;
    ExpectThrows<InvalidDataException>(() => UyaCollisionReader.Read(badIndex));
    var overlappingOctants = (byte[])bytes.Clone();
    WriteUInt32(overlappingOctants, 0x58, 0x2003);
    ExpectThrows<InvalidDataException>(() => UyaCollisionReader.Read(overlappingOctants));
    var badBarrierPadding = (byte[])bytes.Clone();
    badBarrierPadding[0xe6] = 1;
    ExpectThrows<InvalidDataException>(() => UyaCollisionReader.Read(badBarrierPadding));
    var badBarrierCount = (byte[])bytes.Clone();
    WriteInt32(badBarrierCount, 0xc0, int.MaxValue);
    ExpectThrows<InvalidDataException>(() => UyaCollisionReader.Read(badBarrierCount));
    ExpectThrows<InvalidDataException>(() => UyaCollisionReader.Read(new byte[7]));
    var empty = new byte[0x10];
    WriteInt32(empty, 0, 8);
    Expect(UyaCollisionReader.Read(empty).SolidPieces.Count == 0, "UYA collision should accept empty layers");
    ExpectThrows<NotSupportedException>(() => CollisionConverter.ExportGltf(bytes, GameId.DL));

    var shortPalette = new UyaCollisionGltfPalette(
        [Vector3.One],
        generic.SoundTypeSrgbColors,
        generic.PlayerBarrierSrgbColor);
    ExpectThrows<ArgumentException>(() => UyaCollisionGltfExporter.Export(
        collision,
        options: new UyaCollisionGltfExportOptions { Palette = shortPalette }));
}

static void ValidateUyaTieCollisionSurfaceGeneration()
{
    var tie = CreateSyntheticTieSurfaceClass();

    var candidate = UyaTieCollisionGenerator.GenerateSurface(tie, "tie:synthetic", 0, 0x31);
    var face = candidate.Addition.Faces.Single();
    Expect(candidate is
        {
            SourceVertexCount: 4,
            SourceTriangleCount: 4,
            GeneratedVertexCount: 4,
            GeneratedFaceCount: 1,
            MergedQuadCount: 1,
            RemovedDegenerateFaceCount: 1,
            RemovedDuplicateFaceCount: 1,
            MaximumVertexDeviation: 0,
        } && face is { Type: 0x31, IsQuad: true }
            && face.A == new UyaCollisionVertex(0, 0, 0)
            && face.B == new UyaCollisionVertex(0, 64, 0)
            && face.C == new UyaCollisionVertex(64, 64, 0)
            && face.D == new UyaCollisionVertex(64, 0, 0),
        "TIE surface collision should quantize, filter, merge, and use native outward-blocking winding");
    Expect(candidate.Analysis.LogicalFaceCount == candidate.GeneratedFaceCount
        && candidate.Analysis.LogicalVertexCount == candidate.GeneratedVertexCount
        && candidate.Analysis.HardViolationCount == 0
        && candidate.Analysis.Octants.SelectMany(value => value.AdditionIds).Contains("tie:synthetic"),
        "TIE surface candidate should carry exact native octant diagnostics and addition ownership");
    var encoded = UyaCollisionWriter.Write(new(
        [new(0, candidate.Addition.Faces)],
        [],
        0,
        0,
        0));
    Expect(UyaCollisionReader.Read(encoded).SolidPieces.SelectMany(piece => piece.Faces).Single().IsQuad,
        "generated TIE collision quads should survive native write and re-read");
    var oversized = UyaTieCollisionGenerator.GenerateSurface(
        CreateSyntheticTieSurfaceClass(scale: 131_072), "tie:oversized", 0, 0x31);
    var oversizedEncoded = UyaCollisionWriter.Write(new(
        [new(0, oversized.Addition.Faces)], [], 0, 0, 0));
    Expect(oversized.GeneratedFaceCount > 1
        && oversized.Analysis.HardViolationCount == 0
        && UyaCollisionReader.Read(oversizedEncoded).SolidPieces.SelectMany(piece => piece.Faces).Any(),
        "oversized TIE surface faces should subdivide into native-octant-safe collision");
    var level41TiePath = Path.Combine(
        "test-assets", "extractions_uya", "level41_iso_world01", "assets", "tie", "06236_185C", "tie.bin");
    if (File.Exists(level41TiePath))
    {
        var level41Tie = TieClassReader.Read(
            File.ReadAllBytes(level41TiePath),
            TieClassReadOptions.ForGameProfile(TieGameProfile.ForGame(GameId.UYA)));
        var level41Candidate = UyaTieCollisionGenerator.GenerateDecimatedSurface(
            level41Tie, "tie:level41:185c", rawType: 0x31);
        var level41Encoded = UyaCollisionWriter.Write(new(
            [new(0, level41Candidate.Addition.Faces)], [], 0, 0, 0));
        Expect(level41Candidate.Analysis.HardViolationCount == 0
            && UyaCollisionReader.Read(level41Encoded).SolidPieces.SelectMany(piece => piece.Faces).Any(),
            "level41 TIE 0x185C should generate native-octant-safe collision");
    }
    var placed = UyaCollisionAdditionTransformer.Transform(
        candidate.Addition,
        "tie:placed",
        new(10, 20, 30),
        Quaternion.Identity,
        Vector3.One);
    var recovered = UyaCollisionLinkRecovery.FindCandidates(
        tie,
        new([new(0, placed.Faces)], [], 0, 0, 0),
        [
            new("matching", new(new(10, 20, 30), Quaternion.Identity, Vector3.One)),
            new("overlapping", new(new(10.1f, 20, 30), Quaternion.Identity, Vector3.One)),
            new("distant", new(new(100, 200, 300), Quaternion.Identity, Vector3.One)),
        ]);
    Expect(recovered.Count == 2
        && recovered.Single(value => value.InstanceId == "matching").Confidence
            > recovered.Single(value => value.InstanceId == "overlapping").Confidence,
        "TIE collision recovery should reject distant pieces and rank the centered overlap first");
    var repeated = UyaTieCollisionGenerator.GenerateSurface(tie, "tie:synthetic", 0, 0x31);
    Expect(candidate.Addition.Faces.SequenceEqual(repeated.Addition.Faces),
        "TIE surface collision generation should be deterministic");
    var fine = tie.LodTopologies.Single();
    var multiLod = new TieClass
    {
        Header = tie.Header,
        ByteLength = tie.ByteLength,
        LodTopologies =
        [
            fine,
            new TieLodTopology
            {
                LodIndex = 1,
                LogicalVertexCount = 3,
                PacketVertexRowCount = 3,
                PrimaryAddressMappedLogicalVertexCount = 3,
                SecondaryAddressMappedLogicalVertexCount = 0,
                UnresolvedLogicalVertexCount = 0,
                StripCount = 1,
                TriangleCount = 1,
                LogicalVertices = fine.LogicalVertices.Take(3).ToArray(),
                Triangles = [new(1, 0, 0, 0, 1, 2)],
            },
        ],
    };
    var decimated = UyaTieCollisionGenerator.GenerateDecimatedSurface(
        multiLod, "tie:decimated", rawType: 0x31);
    Expect(decimated is { LodIndex: 1, SourceTriangleCount: 1, GeneratedFaceCount: 1 },
        "decimated TIE collision should use the coarsest usable authored LOD");
    var nonCoplanar = UyaTieCollisionGenerator.GenerateSurface(
        CreateSyntheticTieSurfaceClass(fourthZ: 1),
        "tie:non-coplanar",
        0,
        0x31);
    Expect(nonCoplanar is { GeneratedFaceCount: 2, MergedQuadCount: 0 }
        && nonCoplanar.Addition.Faces.All(value => !value.IsQuad),
        $"TIE surface collision should not merge non-coplanar triangles "
        + $"(faces {nonCoplanar.GeneratedFaceCount}, merged {nonCoplanar.MergedQuadCount})");
    var quantized = UyaTieCollisionGenerator.GenerateSurface(
        CreateSyntheticTieSurfaceClass(scale: 1000),
        "tie:quantized",
        0,
        0x31);
    Expect(quantized.MaximumVertexDeviation is > 0 and < 0.04f,
        "TIE surface collision should report target-quantization deviation");
    ExpectThrows<InvalidDataException>(() => UyaTieCollisionGenerator.GenerateSurface(
        CreateSyntheticTieSurfaceClass(fourthZ: 1),
        "tie:limited",
        0,
        0x31,
        maximumFaces: 1));
    ExpectThrows<ArgumentOutOfRangeException>(() => UyaTieCollisionGenerator.GenerateSurface(
        tie,
        "tie:invalid-limit",
        0,
        0x31,
        maximumFaces: 0));
    ExpectThrows<ArgumentOutOfRangeException>(() => UyaTieCollisionGenerator.GenerateSurface(
        tie,
        "tie:synthetic",
        1,
        0x31));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    ExpectThrows<OperationCanceledException>(() => UyaTieCollisionGenerator.GenerateSurface(
        tie,
        "tie:synthetic",
        0,
        0x31,
        cancellationToken: cancelled.Token));
    ExpectThrows<NotSupportedException>(() => CollisionConverter.GenerateTieSurfaceCandidate(
        [],
        GameId.DL,
        "tie:synthetic"));
}

static void ValidateUyaTieCollisionConvexHullGeneration()
{
    var cube = UyaTieCollisionHullGenerator.Generate(
        CreateSyntheticTieCubeClass(),
        "tie:cube-hull",
        rawType: 0x31,
        profileSections: 1);
    Expect(cube is
        {
            GeneratedVertexCount: 8,
            GeneratedFaceCount: 12,
            MergedQuadCount: 0,
            MaximumSourceVertexDeviation: < 0.001f,
        }
        && cube.Addition.Faces.All(face => face is { Type: 0x31, IsQuad: false })
        && cube.Analysis.HardViolationCount == 0,
        $"convex hull should reduce a cube to twelve native triangles "
        + $"({cube.GeneratedVertexCount} vertices, {cube.GeneratedFaceCount} faces)");

    var pyramid = CreateSyntheticTieClass(
        [
            ((short)-4, (short)-4, (short)0),
            ((short)4, (short)-4, (short)0),
            ((short)4, (short)4, (short)0),
            ((short)-4, (short)4, (short)0),
            ((short)0, (short)0, (short)8),
        ],
        [
            new(0, 0, 0, 0, 2, 1), new(0, 0, 1, 0, 3, 2),
            new(0, 0, 2, 0, 1, 4), new(0, 0, 3, 1, 2, 4),
            new(0, 0, 4, 2, 3, 4), new(0, 0, 5, 3, 0, 4),
        ],
        stripCount: 1);
    var smooth = UyaTieCollisionHullGenerator.Generate(
        pyramid, "tie:pyramid-hull", rawType: 0x31, profileSections: 6);
    Expect(smooth is { ProfileSections: 1, GeneratedFaceCount: 6 }
        && smooth.Addition.Faces.All(face => !face.IsQuad)
        && smooth.Addition.Faces.Any(face =>
            new[] { face.A.Z64, face.B.Z64, face.C.Z64 }.Distinct().Count() > 1),
        $"convex hull should discard redundant sections and retain a smooth sloped silhouette "
        + $"({smooth.GeneratedVertexCount} vertices, {smooth.GeneratedFaceCount} faces, "
        + $"{smooth.MergedQuadCount} quads)");

    var clean = UyaTieCollisionHullGenerator.Generate(
        CreateSyntheticTieBoxes((0, 10, 0, 10, 0, 10)),
        "tie:clean-hull",
        rawType: 0x31,
        profileSections: 1);
    var nested = UyaTieCollisionHullGenerator.Generate(
        CreateSyntheticTieBoxes(
            (0, 10, 0, 10, 0, 10),
            (2, 8, 2, 8, 2, 8),
            (4, 6, 4, 6, 4, 6)),
        "tie:nested-hull",
        rawType: 0x31,
        profileSections: 1);
    Expect(clean.Addition.Faces.SequenceEqual(nested.Addition.Faces),
        "convex hull should discard enclosed render geometry");

    var insetTie = CreateSyntheticTieBoxes(
        (-8, 8, -8, 8, 0, 4),
        (-4, 4, -4, 4, 4, 8),
        (-8, 8, -8, 8, 8, 12));
    var coarseInset = UyaTieCollisionHullGenerator.Generate(
        insetTie, "tie:coarse-inset-hull", rawType: 0x31, profileSections: 1);
    var detailedInset = UyaTieCollisionHullGenerator.Generate(
        insetTie, "tie:detailed-inset-hull", rawType: 0x31, profileSections: 6);
    var coarseVertices = coarseInset.Addition.Faces.SelectMany(face => new[] { face.A, face.B, face.C });
    var detailedVertices = detailedInset.Addition.Faces.SelectMany(face => new[] { face.A, face.B, face.C });
    var maximumX = detailedVertices.Max(vertex => Math.Abs(vertex.X64));
    Expect(coarseInset.ProfileSections == 1
        && detailedInset.ProfileSections is > 1 and <= 6
        && detailedInset.GeneratedFaceCount > coarseInset.GeneratedFaceCount
        && !coarseVertices.Any(vertex => Math.Abs(vertex.X64) < maximumX)
        && detailedVertices.Any(vertex => Math.Abs(vertex.X64) < maximumX),
        "additional hull profile sections should follow a vertical inset instead of bridging it");

    var denseProfile = UyaTieCollisionHullGenerator.Generate(
        CreateSyntheticTiePrism(32, 64, 32),
        "tie:dense-profile-hull",
        rawType: 0x31,
        profileSections: 6);
    Expect(denseProfile is
        {
            ProfileSections: 1,
            GeneratedVertexCount: <= 512,
            GeneratedFaceCount: <= 1024,
        }
        && denseProfile.Analysis.HardViolationCount == 0,
        $"redundant hull sections should collapse while retaining bounded radial complexity "
        + $"({denseProfile.GeneratedVertexCount} vertices, {denseProfile.GeneratedFaceCount} faces)");

    var planar = UyaTieCollisionHullGenerator.Generate(
        CreateSyntheticTieSurfaceClass(),
        "tie:planar-hull",
        rawType: 0x31);
    Expect(planar is { GeneratedFaceCount: 1, MergedQuadCount: 1 },
        "planar hull input should fall back to the source surface");
    ExpectThrows<NotSupportedException>(() => CollisionWork.GenerateTieConvexHullCandidate(
        [], GameId.DL, "tie:unsupported-hull"));
    ExpectThrows<ArgumentOutOfRangeException>(() => UyaTieCollisionHullGenerator.Generate(
        CreateSyntheticTieCubeClass(), "tie:invalid-hull-detail", profileSections: 17));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    ExpectThrows<OperationCanceledException>(() => UyaTieCollisionHullGenerator.Generate(
        CreateSyntheticTieCubeClass(),
        "tie:cancelled-hull",
        cancellationToken: cancelled.Token));
}

static void ValidateUyaShrubCollisionGeneration()
{
    var shrub = new ShrubClass
    {
        Header = new ShrubClassHeader
        {
            BoundingSphere = Vector4.Zero,
            Scale = 1024,
            PacketCount = 1,
        },
        ByteLength = ShrubClassHeader.Size,
        Normals = [new(0, 0, short.MaxValue, 0)],
        Packets =
        [
            new ShrubPacket
            {
                PacketIndex = 0,
                Entry = new(0, 0),
                Header = new(0, 0, 6, 0),
                Primitives =
                [
                    new ShrubVertexPrimitive(0, ShrubGeometryType.TriangleList,
                    [
                        Vertex(0, 0), Vertex(1, 0), Vertex(1, 1),
                        Vertex(0, 0), Vertex(1, 1), Vertex(0, 1),
                    ]),
                ],
            },
        ],
    };
    var surface = UyaShrubCollisionGenerator.GenerateSurface(shrub, "shrub:synthetic", rawType: 0x31);
    Expect(surface is
        {
            LodIndex: 0,
            SourceVertexCount: 6,
            SourceTriangleCount: 2,
            GeneratedVertexCount: 4,
            GeneratedFaceCount: 1,
            MergedQuadCount: 1,
        } && surface.Addition.Faces.Single() is { Type: 0x31, IsQuad: true },
        "shrub surface collision should share native filtering, quantization, and quad merging");
    var hull = UyaShrubCollisionGenerator.GenerateHull(shrub, "shrub:hull", rawType: 0x31);
    Expect(hull is { GeneratedFaceCount: 1, MergedQuadCount: 1 },
        "planar shrub hull generation should fall back to its decoded surface");
    var placed = UyaCollisionAdditionTransformer.Transform(
        surface.Addition,
        "shrub:placed",
        new(10, 20, 30),
        Quaternion.Identity,
        new(0.25f));
    var recovered = UyaCollisionLinkRecovery.FindShrubCandidates(
        shrub,
        new([new(0, placed.Faces)], [], 0, 0, 0),
        [new("matching", new(new(10, 20, 30), Quaternion.Identity, Vector3.One))]);
    Expect(recovered is [{ InstanceId: "matching", SourcePieceIndex: 0 }],
        "shrub collision recovery should match a small collision fragment inside a transformed shrub instance");

    static ShrubVertex Vertex(short x, short y) => new(x, y, 0, 0, 0, 0, 0);
}

static TieClass CreateSyntheticTieSurfaceClass(short fourthZ = 0, float scale = 1024) => new()
{
    Header = new TieClassHeader
    {
        PacketTableOffsets = [],
        PacketCounts = [],
        CacheSizes = [],
        RgbaRemapOffsets = [],
        Scale = scale,
        Lods = [],
        UnknownOffsets78 = [],
    },
    ByteLength = TieClassHeader.Size,
    LodTopologies =
    [
        new TieLodTopology
        {
            LodIndex = 0,
            LogicalVertexCount = 4,
            PacketVertexRowCount = 4,
            PrimaryAddressMappedLogicalVertexCount = 4,
            SecondaryAddressMappedLogicalVertexCount = 0,
            UnresolvedLogicalVertexCount = 0,
            StripCount = 1,
            TriangleCount = 4,
            LogicalVertices =
            [
                CreateSyntheticTieLogicalVertex(0, 0, 0, 0),
                CreateSyntheticTieLogicalVertex(1, 1, 0, 0),
                CreateSyntheticTieLogicalVertex(2, 1, 1, 0),
                CreateSyntheticTieLogicalVertex(3, 0, 1, fourthZ),
            ],
            Triangles =
            [
                new(0, 0, 0, 0, 1, 2),
                new(0, 0, 1, 0, 2, 3),
                new(0, 0, 2, 2, 1, 0),
                new(0, 0, 3, 0, 0, 1),
            ],
        },
    ],
};

static TieClass CreateSyntheticTieCubeClass(
    bool openTop = false,
    bool includeBridge = false,
    bool mirrorX = false,
    short size = 4) => new()
{
    Header = new TieClassHeader
    {
        PacketTableOffsets = [],
        PacketCounts = [],
        CacheSizes = [],
        RgbaRemapOffsets = [],
        Scale = 1024,
        Lods = [],
        UnknownOffsets78 = [],
    },
    ByteLength = TieClassHeader.Size,
    LodTopologies =
    [
        new TieLodTopology
        {
            LodIndex = 0,
            LogicalVertexCount = includeBridge ? 12 : 8,
            PacketVertexRowCount = includeBridge ? 12 : 8,
            PrimaryAddressMappedLogicalVertexCount = includeBridge ? 12 : 8,
            SecondaryAddressMappedLogicalVertexCount = 0,
            UnresolvedLogicalVertexCount = 0,
            StripCount = 1,
            TriangleCount = (openTop ? 10 : 12) + (includeBridge ? 2 : 0),
            LogicalVertices =
            [
                CreateSyntheticTieLogicalVertex(0, 0, 0, 0),
                CreateSyntheticTieLogicalVertex(1, (short)(mirrorX ? -size : size), 0, 0),
                CreateSyntheticTieLogicalVertex(2, (short)(mirrorX ? -size : size), size, 0),
                CreateSyntheticTieLogicalVertex(3, 0, size, 0),
                CreateSyntheticTieLogicalVertex(4, 0, 0, size),
                CreateSyntheticTieLogicalVertex(5, (short)(mirrorX ? -size : size), 0, size),
                CreateSyntheticTieLogicalVertex(6, (short)(mirrorX ? -size : size), size, size),
                CreateSyntheticTieLogicalVertex(7, 0, size, size),
                ..(includeBridge
                    ? new[]
                    {
                        CreateSyntheticTieLogicalVertex(8, 0, 0, (short)(size * 2)),
                        CreateSyntheticTieLogicalVertex(
                            9,
                            (short)(mirrorX ? -size : size),
                            0,
                            (short)(size * 2)),
                        CreateSyntheticTieLogicalVertex(
                            10,
                            (short)(mirrorX ? -size : size),
                            size,
                            (short)(size * 2)),
                        CreateSyntheticTieLogicalVertex(11, 0, size, (short)(size * 2)),
                    }
                    : Array.Empty<TieLogicalVertex>()),
            ],
            Triangles =
            [
                new(0, 0, 0, 0, 2, 1), new(0, 0, 1, 0, 3, 2),
                ..(openTop
                    ? Array.Empty<TieTriangle>()
                    : new TieTriangle[] { new(0, 0, 2, 4, 5, 6), new(0, 0, 3, 4, 6, 7) }),
                new(0, 0, 4, 0, 1, 5), new(0, 0, 5, 0, 5, 4),
                new(0, 0, 6, 1, 2, 6), new(0, 0, 7, 1, 6, 5),
                new(0, 0, 8, 2, 3, 7), new(0, 0, 9, 2, 7, 6),
                new(0, 0, 10, 3, 0, 4), new(0, 0, 11, 3, 4, 7),
                ..(includeBridge
                    ? new TieTriangle[] { new(0, 0, 12, 8, 10, 9), new(0, 0, 13, 8, 11, 10) }
                    : Array.Empty<TieTriangle>()),
            ],
        },
    ],
};

static TieClass CreateSyntheticTieBoxes(
    params (short MinimumX, short MaximumX, short MinimumY, short MaximumY, short MinimumZ, short MaximumZ)[] boxes)
{
    var positions = boxes.SelectMany(box => new[]
    {
        (box.MinimumX, box.MinimumY, box.MinimumZ),
        (box.MaximumX, box.MinimumY, box.MinimumZ),
        (box.MaximumX, box.MaximumY, box.MinimumZ),
        (box.MinimumX, box.MaximumY, box.MinimumZ),
        (box.MinimumX, box.MinimumY, box.MaximumZ),
        (box.MaximumX, box.MinimumY, box.MaximumZ),
        (box.MaximumX, box.MaximumY, box.MaximumZ),
        (box.MinimumX, box.MaximumY, box.MaximumZ),
    }).ToArray();
    var triangleIndex = 0;
    var triangles = boxes.SelectMany((_, boxIndex) =>
    {
        var first = boxIndex * 8;
        return new[]
        {
            new TieTriangle(0, 0, triangleIndex++, first, first + 2, first + 1),
            new TieTriangle(0, 0, triangleIndex++, first, first + 3, first + 2),
            new TieTriangle(0, 0, triangleIndex++, first + 4, first + 5, first + 6),
            new TieTriangle(0, 0, triangleIndex++, first + 4, first + 6, first + 7),
            new TieTriangle(0, 0, triangleIndex++, first, first + 1, first + 5),
            new TieTriangle(0, 0, triangleIndex++, first, first + 5, first + 4),
            new TieTriangle(0, 0, triangleIndex++, first + 1, first + 2, first + 6),
            new TieTriangle(0, 0, triangleIndex++, first + 1, first + 6, first + 5),
            new TieTriangle(0, 0, triangleIndex++, first + 2, first + 3, first + 7),
            new TieTriangle(0, 0, triangleIndex++, first + 2, first + 7, first + 6),
            new TieTriangle(0, 0, triangleIndex++, first + 3, first, first + 4),
            new TieTriangle(0, 0, triangleIndex++, first + 3, first + 4, first + 7),
        };
    }).ToArray();

    return CreateSyntheticTieClass(positions, triangles, boxes.Length);
}

static TieClass CreateSyntheticTiePrism(int sides, short radius, short height)
{
    var positions = Enumerable.Range(0, sides).SelectMany(index =>
    {
        var angle = MathF.Tau * index / sides;
        var x = checked((short)MathF.Round(MathF.Cos(angle) * radius));
        var y = checked((short)MathF.Round(MathF.Sin(angle) * radius));
        return new[] { (x, y, (short)0), (x, y, height) };
    }).ToArray();
    var triangles = new List<TieTriangle>();
    for (var index = 0; index < sides; index++)
    {
        var next = (index + 1) % sides;
        triangles.Add(new(0, 0, triangles.Count, index * 2, next * 2, next * 2 + 1));
        triangles.Add(new(0, 0, triangles.Count, index * 2, next * 2 + 1, index * 2 + 1));
    }
    for (var index = 1; index + 1 < sides; index++)
    {
        triangles.Add(new(0, 0, triangles.Count, 0, (index + 1) * 2, index * 2));
        triangles.Add(new(0, 0, triangles.Count, 1, index * 2 + 1, (index + 1) * 2 + 1));
    }
    return CreateSyntheticTieClass(positions, triangles.ToArray(), stripCount: 1);
}

static TieClass CreateSyntheticTieClass(
    (short X, short Y, short Z)[] positions,
    TieTriangle[] triangles,
    int stripCount) => new()
{
    Header = new TieClassHeader
    {
        PacketTableOffsets = [],
        PacketCounts = [],
        CacheSizes = [],
        RgbaRemapOffsets = [],
        Scale = 1024,
        Lods = [],
        UnknownOffsets78 = [],
    },
    ByteLength = TieClassHeader.Size,
    LodTopologies =
    [
        new TieLodTopology
        {
            LodIndex = 0,
            LogicalVertexCount = positions.Length,
            PacketVertexRowCount = positions.Length,
            PrimaryAddressMappedLogicalVertexCount = positions.Length,
            SecondaryAddressMappedLogicalVertexCount = 0,
            UnresolvedLogicalVertexCount = 0,
            StripCount = stripCount,
            TriangleCount = triangles.Length,
            LogicalVertices = positions.Select((position, index) =>
                CreateSyntheticTieLogicalVertex(index, position.X, position.Y, position.Z)).ToArray(),
            Triangles = triangles,
        },
    ],
};

static TieLogicalVertex CreateSyntheticTieLogicalVertex(int index, short x, short y, short z) => new()
{
    LodIndex = 0,
    PacketIndex = 0,
    PacketStripIndex = 0,
    StripIndex = 0,
    IndexInStrip = index,
    LogicalVertexIndex = index,
    VuAddress = index,
    Token = 0,
    MappingKind = TieLogicalVertexMappingKind.PrimaryRowAddress,
    DecodedVertex = new()
    {
        Index = index,
        SourceIndex = index,
        Kind = TiePacketDecodedVertexKind.Dinky,
        Offset = 0,
        Bytes = [],
        SourceRowIndex = index,
        SourceRow = null,
        X = x,
        Y = y,
        Z = z,
        GsPacketWriteOffset = (ushort)index,
        S = 0,
        T = 0,
        Q = 0,
        SecondaryGsPacketWriteOffset = 0,
    },
    AddressRow = null,
    VertexRow = null,
};


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
    Expect(hud.Header.IconMappingCount == 1, "DL HUD icon count should exclude the terminator");
    Expect(hud.Header.FrameCount == 1, "DL HUD frame count should be parsed");
    Expect(hud.Icons[0].IconId == 0x1234, "DL HUD icon id should be parsed");
    Expect(hud.Icons[0].FrameCount == 1 && hud.Icons[0].FirstFrameIndex == 0, "DL HUD icon frame range should be parsed");
    Expect(hud.Icons.Count == 1, "DL HUD icon terminator should not be exposed as a mapping");
    Expect(hud.IconTerminatorOffset == 0xbc, "DL HUD icon terminator offset should be preserved");
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

    var fiveBankHeader = new byte[0x11c];
    WriteUInt16(fiveBankHeader, 0x00, 1);
    WriteUInt16(fiveBankHeader, 0x02, 5);
    WriteInt32(fiveBankHeader, 0x04, 0xb4);
    WriteInt32(fiveBankHeader, 0x08, 0xb8);
    WriteInt32(fiveBankHeader, 0x0c, 0xcc);
    WriteInt32(fiveBankHeader, 0x10, 0xf4);
    WriteUInt32(fiveBankHeader, 0xb4, HudBankReader.IconMappingTerminator);
    var fiveBanks = new byte[HudBankReader.BankCount][];
    for (var bankIndex = 0; bankIndex < HudBankReader.BankCount; bankIndex++)
    {
        WriteInt32(fiveBankHeader, 0x14 + bankIndex * 4, bankIndex + 1);
        WriteInt32(fiveBankHeader, 0x34 + bankIndex * 4, bankIndex + 1);
        WriteInt32(fiveBankHeader, 0x54 + bankIndex * 4, 0x440);
        WriteInt16(fiveBankHeader, 0xb8 + bankIndex * 4, (short)bankIndex);
        WriteInt16(fiveBankHeader, 0xba + bankIndex * 4, (short)bankIndex);
        WriteUInt32(fiveBankHeader, 0xcc + bankIndex * 8, 0x80000000);
        WriteUInt32(fiveBankHeader, 0xf4 + bankIndex * 8, 0x80000400);
        fiveBankHeader[0xfa + bankIndex * 8] = 3;
        fiveBankHeader[0xfb + bankIndex * 8] = 3;
        fiveBanks[bankIndex] = Enumerable.Repeat((byte)(bankIndex + 1), 0x440).ToArray();
    }

    var fiveBankHud = HudBankReader.Read(fiveBankHeader, fiveBanks);
    for (var bankIndex = 0; bankIndex < HudBankReader.BankCount; bankIndex++)
    {
        Expect(fiveBankHud.Palettes[bankIndex].BankIndex == bankIndex,
            $"HUD palette {bankIndex} should route to physical bank {bankIndex}");
        Expect(fiveBankHud.Textures[bankIndex].BankIndex == bankIndex,
            $"HUD texture {bankIndex} should route to physical bank {bankIndex}");
        Expect(fiveBankHud.Textures[bankIndex].PixelBytes.All(value => value == bankIndex + 1),
            $"HUD texture {bankIndex} should read bytes from physical bank {bankIndex}");
    }

    var invalidTerminatorHeader = fiveBankHeader.ToArray();
    WriteUInt32(invalidTerminatorHeader, 0xb4, 0);
    ExpectThrows<InvalidDataException>(() => HudBankReader.Read(invalidTerminatorHeader, fiveBanks));

    var oversizedTableHeader = fiveBankHeader.ToArray();
    WriteInt32(oversizedTableHeader, 0x24, int.MaxValue);
    ExpectThrows<InvalidDataException>(() => HudBankReader.Read(oversizedTableHeader, fiveBanks));
}

static void ValidateHudSpriteIds()
{
    Expect(UyaHudSpriteId.TryParse("ed1b", out var uyaId) && uyaId == 0xed1b,
        "UYA HUD sprite IDs should parse case-insensitive four-digit hexadecimal text");
    Expect(UyaHudSpriteId.Format(uyaId) == "ED1B", "UYA HUD sprite IDs should format canonically");
    Expect(UyaHudSpriteId.IsValidCustomValue(0xe000) && UyaHudSpriteId.IsValidCustomValue(0xefff),
        "UYA HUD custom sprite-ID boundaries should be valid");
    Expect(!UyaHudSpriteId.IsValidCustomValue(0xdfff) && !UyaHudSpriteId.IsValidCustomValue(0xf000),
        "UYA HUD sprite IDs outside Exxx should be invalid for new mappings");
    Expect(!UyaHudSpriteId.TryParse("0xE000", out _),
        "HUD sprite-ID input should not accept a non-canonical 0x prefix");
    Expect(UyaHudSpriteId.IsReserved(0xffff, []), "the HUD icon terminator should be reserved in UYA");
    Expect(!UyaHudSpriteId.IsAvailable(0xe123, [0xe123]),
        "an occupied UYA HUD sprite ID should not be available");
    Expect(UyaHudSpriteId.IsAvailable(0xe124, [0xe123]),
        "an unoccupied UYA HUD sprite ID should be available");

    Expect(DlHudSpriteId.TryParse("759D", out var dlId) && dlId == 0x759d,
        "DL HUD sprite IDs should parse four-digit hexadecimal text");
    Expect(DlHudSpriteId.Format(dlId) == "759D", "DL HUD sprite IDs should format canonically");
    Expect(DlHudSpriteId.IsValidCustomValue(0x7500) && DlHudSpriteId.IsValidCustomValue(0x75ff),
        "DL HUD custom sprite-ID boundaries should be valid");
    Expect(!DlHudSpriteId.IsValidCustomValue(0x74ff) && !DlHudSpriteId.IsValidCustomValue(0x7600),
        "DL HUD sprite IDs outside 75xx should be invalid for new mappings");
    Expect(DlHudSpriteId.IsReserved(0xffff, []), "the HUD icon terminator should be reserved in DL");
    Expect(!DlHudSpriteId.IsAvailable(0x759d, [0x759d]),
        "an occupied DL HUD sprite ID should not be available");
    Expect(DlHudSpriteId.IsAvailable(0x759e, [0x759d]),
        "an unoccupied DL HUD sprite ID should be available");
}

static void ValidateHudBankComposition()
{
    ValidateHudNoEditCorpusWhenAvailable();
    var fixture = CreateHudCompositionFixture();
    var noEdit = HudComposer.Compose(GameId.UYA, fixture.Header, fixture.Banks, [], []);
    Expect(noEdit.IsBasePassThrough, "no-edit HUD composition should identify the byte-preserving path");
    Expect(noEdit.HeaderBytes.SequenceEqual(fixture.Header),
        "no-edit HUD composition should preserve the header byte-for-byte");
    Expect(noEdit.BankBytes.Select((bytes, index) => bytes.SequenceEqual(fixture.Banks[index])).All(value => value),
        "no-edit HUD composition should preserve every stored bank byte-for-byte");

    var replacementTexture = CreateHudIndexedTexture(8, 8, 0x81);
    var replaced = HudComposer.Compose(
        GameId.UYA,
        fixture.Header,
        fixture.Banks,
        [new HudTextureReplacement(0, replacementTexture)],
        []);
    var replacedHud = ReadComposedHud(replaced);
    var sourceBanks = ReadHudBanks(fixture.Banks);
    var replacedBanks = ReadHudBanks(replaced.BankBytes);
    Expect(!replaced.IsBasePassThrough, "edited HUD composition should not report pass-through");
    Expect(BinaryMagic.IsWad(replaced.BankBytes[0]) && !BinaryMagic.IsWad(replaced.BankBytes[1]),
        "HUD composition should preserve each changed bank's compressed or raw storage mode");
    Expect(replacedBanks[0].AsSpan(0, sourceBanks[0].Length).SequenceEqual(sourceBanks[0])
        && replacedBanks[1].AsSpan(0, sourceBanks[1].Length).SequenceEqual(sourceBanks[1]),
        "HUD replacement should preserve original bank bytes as immutable prefixes");
    Expect(replaced.HeaderBytes[0x68] == fixture.Header[0x68]
        && replacedHud.Icons[0].IconId == 0xed1b
        && replacedHud.Frames[0].PaletteIndex == 1
        && replacedHud.Frames[0].TextureIndex == 1,
        "HUD replacement should preserve opaque header state and remap only the edited frame");
    Expect(replacedHud.Textures[1].Width == 8 && replacedHud.Textures[1].Height == 8
        && replacedHud.Palettes[1].PaletteBytes.SequenceEqual(replacementTexture.PaletteBytes.ToArray())
        && replacedHud.Textures[1].PixelBytes.SequenceEqual(replacementTexture.PixelBytes.ToArray()),
        "HUD replacement should reread from isolated appended palette and texture records");
    Expect(replacedHud.Palettes[0].GsRam == 0x1234
        && replacedHud.Palettes[0].Padding == 0xbeef
        && replacedHud.Textures[0].GsRam == 0x5678
        && replacedHud.Palettes[1].GsRam == 0
        && replacedHud.Textures[1].GsRam == 0,
        "HUD replacement should preserve source runtime metadata and clear new runtime scratch");
    Expect(replaced.BankBytes.Skip(2).Select((bytes, index) => bytes.SequenceEqual(fixture.Banks[index + 2])).All(value => value),
        "HUD replacement should preserve unaffected stored banks byte-for-byte");

    var sharedFixture = CreateSharedHudCompositionFixture();
    var sharedSource = ReadComposedHud(new(sharedFixture.Header, sharedFixture.Banks, true));
    var isolated = ReadComposedHud(HudComposer.Compose(
        GameId.UYA,
        sharedFixture.Header,
        sharedFixture.Banks,
        [new HudTextureReplacement(0, replacementTexture)],
        []));
    Expect(isolated.Frames[0].PaletteIndex == 1 && isolated.Frames[0].TextureIndex == 1
        && isolated.Frames[1] == sharedSource.Frames[1]
        && isolated.Palettes[0].PaletteBytes.SequenceEqual(sharedSource.Palettes[0].PaletteBytes)
        && isolated.Textures[0].PixelBytes.SequenceEqual(sharedSource.Textures[0].PixelBytes),
        "HUD replacement should isolate a frame that shares both source records with another frame");

    var appendedTexture = CreateHudIndexedTexture(4, 4, 0x42);
    var addition = new HudIconAddition(0xefff, 2, appendedTexture);
    var appended = HudComposer.Compose(GameId.UYA, fixture.Header, fixture.Banks, [], [addition]);
    var appendedAgain = HudComposer.Compose(GameId.UYA, fixture.Header, fixture.Banks, [], [addition]);
    var appendedHud = ReadComposedHud(appended);
    Expect(appended.HeaderBytes.SequenceEqual(appendedAgain.HeaderBytes)
        && appended.BankBytes.Select((bytes, index) => bytes.SequenceEqual(appendedAgain.BankBytes[index])).All(value => value),
        "HUD append placement and serialization should be deterministic");
    Expect(appendedHud.Icons.Count == 2 && appendedHud.Frames.Count == 2
        && appendedHud.Palettes.Count == 2 && appendedHud.Textures.Count == 2,
        "HUD append should add one complete icon/frame/palette/texture chain");
    Expect(appendedHud.Icons[0].IconId == 0xed1b
        && appendedHud.Icons[1].IconId == 0xefff
        && appendedHud.Icons[1].FirstFrameIndex == 1
        && appendedHud.Frames[1].PaletteIndex == 1
        && appendedHud.Frames[1].TextureIndex == 1,
        "HUD append should retain existing indexes and assign stable new indexes");
    Expect(appendedHud.Palettes[1].BankIndex == 2 && appendedHud.Textures[1].BankIndex == 2
        && appendedHud.Palettes[1].PaletteBytes.SequenceEqual(appendedTexture.PaletteBytes.ToArray())
        && appendedHud.Textures[1].PixelBytes.SequenceEqual(appendedTexture.PixelBytes.ToArray()),
        "HUD append should route and reread the new payload from its requested bank");
    Expect(appended.HeaderBytes.AsSpan(0x68, HudBankReader.HeaderFixedLength - 0x68)
            .SequenceEqual(fixture.Header.AsSpan(0x68, HudBankReader.HeaderFixedLength - 0x68)),
        "HUD append should preserve the opaque runtime-pointer region");
    Expect(ReadHudBanks(appended.BankBytes)[2].Length == 0x410,
        "HUD append should place an aligned palette and indexed-8 pixels without hidden padding");

    ExpectThrows<InvalidDataException>(() => HudComposer.Compose(
        GameId.UYA, fixture.Header, fixture.Banks, [],
        [new HudIconAddition(0x759d, 2, appendedTexture)]));
    ExpectThrows<InvalidDataException>(() => HudComposer.Compose(
        GameId.UYA, fixture.Header, fixture.Banks, [],
        [new HudIconAddition(0xed1b, 2, appendedTexture)]));
    ExpectThrows<InvalidDataException>(() => HudComposer.Compose(
        GameId.UYA, fixture.Header, fixture.Banks, [],
        [new HudIconAddition(0xe123, 0, appendedTexture)]));
    ExpectThrows<InvalidDataException>(() => HudComposer.Compose(
        GameId.UYA, fixture.Header, fixture.Banks,
        [new HudTextureReplacement(0, new HudIndexedTexture(4, 4, new byte[1], new byte[16]))], []));
    ExpectThrows<NotSupportedException>(() => HudComposer.Compose(
        GameId.DL, fixture.Header, fixture.Banks, [], []));

    var capacityOptions = new HudBankCompositionOptions(
        MaximumDecompressedBankBytes: 0x1000,
        BankCapacities: [0x1000, 0x1000, 0x200, 0x1000, 0x1000]);
    try
    {
        HudComposer.Compose(
            GameId.UYA, fixture.Header, fixture.Banks, [],
            [new HudIconAddition(0xe123, 2, appendedTexture)], capacityOptions);
        throw new InvalidOperationException("Expected a HUD bank capacity failure.");
    }
    catch (InvalidDataException exception)
    {
        Expect(exception.Message.Contains("HUD addition 0 palette", StringComparison.Ordinal)
            && exception.Message.Contains("bank 2", StringComparison.Ordinal),
            "HUD capacity diagnostics should identify the failing entry and bank");
    }

    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.Cancel();
        ExpectThrows<OperationCanceledException>(() => HudComposer.Compose(
            GameId.UYA, fixture.Header, fixture.Banks, [], [addition], cancellationToken: cancellation.Token));
    }

    const int largeDimension = 2048;
    const int largePixelLength = largeDimension * largeDimension;
    const int largeBankLength = HudBankReader.PaletteLength + largePixelLength;
    var largeTexture = new HudIndexedTexture(
        largeDimension,
        largeDimension,
        new byte[HudBankReader.PaletteLength],
        new byte[largePixelLength]);
    var largeOptions = new HudBankCompositionOptions(
        MaximumDecompressedBankBytes: largeBankLength,
        BankCapacities: [0x40, 0x400, largeBankLength, 0, 0]);
    var stopwatch = Stopwatch.StartNew();
    var large = HudComposer.Compose(
        GameId.UYA, fixture.Header, fixture.Banks, [],
        [new HudIconAddition(0xeffe, 2, largeTexture)], largeOptions);
    stopwatch.Stop();
    Expect(ReadHudBanks(large.BankBytes)[2].Length == largeBankLength,
        "HUD composition should fill an explicitly bounded large bank without over-allocation");
    Expect(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
        "bounded 4 MiB HUD composition should complete without pathological throughput");
}

static void ValidateHudNoEditCorpusWhenAvailable()
{
    var corpusRoot = Path.Combine("test-assets", "extractions_uya");
    if (!Directory.Exists(corpusRoot)) return;

    foreach (var hudDirectory in Directory.EnumerateDirectories(corpusRoot, "hud", SearchOption.AllDirectories)
                 .Order(StringComparer.Ordinal))
    {
        var headerPath = Path.Combine(hudDirectory, "header.bin");
        var bankPaths = Enumerable.Range(0, HudBankReader.BankCount)
            .Select(bank => Path.Combine(hudDirectory, $"bank{bank}.bin"))
            .ToArray();
        if (!File.Exists(headerPath) || bankPaths.Any(path => !File.Exists(path))) continue;

        var header = File.ReadAllBytes(headerPath);
        var banks = bankPaths.Select(File.ReadAllBytes).ToArray();
        var source = HudBankReader.Read(header, ReadHudBanks(banks));
        var composed = HudComposer.Compose(GameId.UYA, header, banks, [], []);
        var reread = ReadComposedHud(composed);
        var fixtureName = Path.GetFileName(Path.GetDirectoryName(hudDirectory));
        Expect(composed.IsBasePassThrough
            && composed.HeaderBytes.SequenceEqual(header)
            && composed.BankBytes.Select((bytes, index) => bytes.SequenceEqual(banks[index])).All(value => value),
            $"UYA HUD fixture {fixtureName} should preserve every no-edit stored byte");
        Expect(reread.Icons.Select(icon => (icon.IconId, icon.FrameCount, icon.FirstFrameIndex))
                .SequenceEqual(source.Icons.Select(icon => (icon.IconId, icon.FrameCount, icon.FirstFrameIndex)))
            && reread.Frames.Select(frame => (frame.PaletteIndex, frame.TextureIndex))
                .SequenceEqual(source.Frames.Select(frame => (frame.PaletteIndex, frame.TextureIndex)))
            && reread.Palettes.Count == source.Palettes.Count
            && reread.Textures.Count == source.Textures.Count,
            $"UYA HUD fixture {fixtureName} should preserve no-edit table semantics");
    }
}

static (byte[] Header, byte[][] Banks) CreateHudCompositionFixture()
{
    var header = new byte[0xd8];
    WriteUInt16(header, 0x00, 2);
    WriteUInt16(header, 0x02, 1);
    WriteInt32(header, 0x04, 0xb4);
    WriteInt32(header, 0x08, 0xc4);
    WriteInt32(header, 0x0c, 0xc8);
    WriteInt32(header, 0x10, 0xd0);
    WriteInt32(header, 0x18, 1);
    WriteInt32(header, 0x1c, 1);
    WriteInt32(header, 0x20, 1);
    WriteInt32(header, 0x24, 1);
    for (var bank = 0; bank < HudBankReader.BankCount; bank++)
        WriteInt32(header, 0x34 + bank * 4, 1);
    WriteInt32(header, 0x54, 0x40);
    WriteInt32(header, 0x58, 0x400);
    header[0x68] = 0xa5;

    WriteUInt16(header, 0xb4, 0xed1b);
    WriteUInt16(header, 0xb6, 1);
    WriteUInt16(header, 0xb8, 0);
    WriteUInt32(header, 0xbc, HudBankReader.IconMappingTerminator);
    WriteInt16(header, 0xc4, 0);
    WriteInt16(header, 0xc6, 0);
    WriteUInt32(header, 0xc8, 0x80000000);
    WriteUInt16(header, 0xcc, 0x1234);
    WriteUInt16(header, 0xce, 0xbeef);
    WriteUInt32(header, 0xd0, 0x80000000);
    WriteUInt16(header, 0xd4, 0x5678);
    header[0xd6] = 2;
    header[0xd7] = 2;

    var bank0 = Enumerable.Range(0, 0x40).Select(value => (byte)value).ToArray();
    var bank1 = CreatePalette();
    return (header, [WadCompression.CompressVerified(bank0).CompressedBytes, bank1, [], [], []]);
}

static (byte[] Header, byte[][] Banks) CreateSharedHudCompositionFixture()
{
    var header = new byte[0xe0];
    WriteUInt16(header, 0x00, 3);
    WriteUInt16(header, 0x02, 2);
    WriteInt32(header, 0x04, 0xb4);
    WriteInt32(header, 0x08, 0xc8);
    WriteInt32(header, 0x0c, 0xd0);
    WriteInt32(header, 0x10, 0xd8);
    for (var bank = 1; bank < HudBankReader.BankCount; bank++)
        WriteInt32(header, 0x14 + bank * 4, 1);
    for (var bank = 0; bank < HudBankReader.BankCount; bank++)
        WriteInt32(header, 0x34 + bank * 4, 1);
    WriteInt32(header, 0x54, 0x40);
    WriteInt32(header, 0x58, 0x400);

    WriteUInt16(header, 0xb4, 0xed1b);
    WriteUInt16(header, 0xb6, 1);
    WriteUInt16(header, 0xb8, 0);
    WriteUInt16(header, 0xbc, 0xed1c);
    WriteUInt16(header, 0xbe, 1);
    WriteUInt16(header, 0xc0, 1);
    WriteUInt32(header, 0xc4, HudBankReader.IconMappingTerminator);
    WriteInt16(header, 0xc8, 0);
    WriteInt16(header, 0xca, 0);
    WriteInt16(header, 0xcc, 0);
    WriteInt16(header, 0xce, 0);
    WriteUInt32(header, 0xd0, 0x80000000);
    WriteUInt32(header, 0xd8, 0x80000000);
    header[0xde] = 2;
    header[0xdf] = 2;

    var bank0 = Enumerable.Range(0, 0x40).Select(value => (byte)value).ToArray();
    return (header, [WadCompression.CompressVerified(bank0).CompressedBytes, CreatePalette(), [], [], []]);
}

static HudIndexedTexture CreateHudIndexedTexture(int width, int height, byte marker)
{
    var palette = Enumerable.Range(0, HudBankReader.PaletteLength)
        .Select(index => unchecked((byte)(marker + index))).ToArray();
    var pixels = Enumerable.Range(0, checked(width * height))
        .Select(index => unchecked((byte)(marker ^ index))).ToArray();
    return new HudIndexedTexture(width, height, palette, pixels);
}

static byte[][] ReadHudBanks(IReadOnlyList<byte[]> banks) => banks
    .Select(bytes => BinaryMagic.IsWad(bytes) ? WadCompression.Decompress(bytes) : bytes.ToArray())
    .ToArray();

static HudBankSet ReadComposedHud(HudBankComposition composition) =>
    HudBankReader.Read(composition.HeaderBytes, ReadHudBanks(composition.BankBytes));

static void ValidateFxTextureCatalog()
{
    var uyaLabels = string.Join('\n', Enumerable.Range(0, 124)
        .Select(index => FxTextureCatalog.GetLabel(GameId.UYA, index)));
    var dlLabels = string.Join('\n', Enumerable.Range(0, 124)
        .Select(index => FxTextureCatalog.GetLabel(GameId.DL, index)));
    Expect(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uyaLabels))).ToLowerInvariant()
            == "6909e759f081ddcf05fa705dd87deacab7f5ef8aff1b674015dc0d90402e9e9f",
        "UYA FX labels should match the reviewed map-o-matic catalog snapshot");
    Expect(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dlLabels))).ToLowerInvariant()
            == "8fb06efcf2937703a9ec7fbad1fc2f2cd78a9512c789feea098524a12bd2b311",
        "DL FX labels should match the reviewed map-o-matic catalog snapshot");
    Expect(FxTextureCatalog.GetLabel(GameId.UYA, -8) == "FX_BACK_ALPHA_CLUT"
        && FxTextureCatalog.GetLabel(GameId.DL, -1) == "FX_BACK_BUFFER_COPY",
        "special negative FX labels should be shared across adapters");
    Expect(FxTextureCatalog.GetLabel(GameId.UYA, 500) == "FX_TEXTURE_500",
        "unknown FX indexes should use a deterministic fallback");
    ExpectThrows<NotSupportedException>(() => FxTextureCatalog.GetLabel(GameId.RC1, 0));

    var fixture = CreateFxTextureFixture();
    var uya = FxTextureCatalog.Read(GameId.UYA, fixture.Header, fixture.Asset);
    Expect(uya.Entries.Count == 2 && uya.Entries.All(entry => entry.IsValid),
        "UYA FX inventory should retain valid ordered definitions");
    Expect(uya.Entries[0].Label == "FX_LAME_SHADOW"
        && uya.Entries[1].Label == "FX_CLOUDY_CIRCLE_1",
        "UYA FX inventory should attach game-owned labels by stable source index");
    Expect(uya.Entries[0] is
        {
            Width: 4, Height: 4, PixelFormat: "Indexed8", PaletteFormat: "Rgba32",
            PaletteOffset: 0, PaletteLength: 0x400, PixelOffset: 0x400, PixelLength: 16,
            IsSwizzled: false,
        }, "UYA FX inventory should expose dimensions, formats, offsets, lengths, and swizzle state");
    var normalized = PifReader.Read(uya.Entries[0].CanonicalTextureBytes);
    Expect(!normalized.Header.IsSwizzled
        && normalized.PaletteData.SequenceEqual(fixture.Asset.AsSpan(0, 0x400).ToArray())
        && normalized.PixelData.SequenceEqual(fixture.Asset.AsSpan(0x400, 16).ToArray()),
        "UYA FX canonical preview should preserve source palette and indexed pixels");
    Expect(uya.Capabilities is
        { CanRead: true, CanReplace: true, CanAppend: true, AuthoringDisabledReason: null },
        "UYA FX inventory should advertise writer capabilities");

    var compressed = WadCompression.CompressVerified(fixture.Asset).CompressedBytes;
    var fromCompressed = FxTextureCatalog.Read(GameId.UYA, fixture.Header, compressed);
    Expect(fromCompressed.Entries.Select(entry => entry.CanonicalTextureBytes)
        .Zip(uya.Entries.Select(entry => entry.CanonicalTextureBytes))
        .All(pair => pair.First.SequenceEqual(pair.Second)),
        "FX inventory should normalize compressed and raw asset payloads identically");

    var dl = FxTextureCatalog.Read(GameId.DL, fixture.Header, fixture.Asset);
    Expect(dl.Entries[1].Label == "FX_GROUND_OUTER_RETICULE"
        && dl.Entries.All(entry => entry.IsSwizzled)
        && PifReader.Read(dl.Entries[0].CanonicalTextureBytes).Header.IsSwizzled,
        "DL FX inventory should apply DL labels and swizzled preview metadata");
    Expect(dl.Capabilities is
        { CanRead: true, CanReplace: false, CanAppend: false, AuthoringDisabledReason: not null },
        "DL FX inventory should remain read-only until its project pipeline is writable");

    var malformedEntryHeader = fixture.Header.ToArray();
    WriteInt32(malformedEntryHeader, 0xc0 + 0x10 + 8, 3);
    var malformedEntry = FxTextureCatalog.Read(GameId.UYA, malformedEntryHeader, fixture.Asset);
    Expect(malformedEntry.Entries[0].IsValid
        && malformedEntry.Entries[1] is { IsValid: false, CanonicalTextureBytes.Length: 0 }
        && malformedEntry.Entries[1].Diagnostic!.Contains("FX texture 1", StringComparison.Ordinal),
        "malformed FX entries should remain ordered and identify their source index");

    var malformedOffsetHeader = fixture.Header.ToArray();
    WriteInt32(malformedOffsetHeader, 0xc0 + 4, 0x401);
    var malformedOffset = FxTextureCatalog.Read(GameId.UYA, malformedOffsetHeader, fixture.Asset);
    Expect(!malformedOffset.Entries[0].IsValid
        && malformedOffset.Entries[0].Diagnostic!.Contains("aligned", StringComparison.Ordinal),
        "misaligned FX offsets should produce entry diagnostics");

    var badCount = fixture.Header.ToArray();
    WriteInt32(badCount, 0x58, 4_097);
    ExpectThrows<InvalidDataException>(() => FxTextureCatalog.Read(GameId.UYA, badCount, fixture.Asset));
    var badTable = fixture.Header.ToArray();
    WriteInt32(badTable, 0x5c, badTable.Length - 1);
    ExpectThrows<InvalidDataException>(() => FxTextureCatalog.Read(GameId.UYA, badTable, fixture.Asset));
    ExpectThrows<InvalidDataException>(() => FxTextureCatalog.Read(
        GameId.UYA,
        fixture.Header,
        fixture.Asset,
        new(MaximumStoredAssetBytes: fixture.Asset.Length - 1)));
    ExpectThrows<NotSupportedException>(() => FxTextureCatalog.Read(GameId.RC1, fixture.Header, fixture.Asset));
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.Cancel();
        ExpectThrows<OperationCanceledException>(() => FxTextureCatalog.Read(
            GameId.UYA, fixture.Header, fixture.Asset, cancellationToken: cancellation.Token));
    }

    ValidateFxTextureCorpusWhenAvailable();
}

static void ValidateFxTextureComposition()
{
    var fixture = CreateFxTextureFixture();
    var noEdit = FxTextureCatalog.Compose(GameId.UYA, fixture.Header, fixture.Asset, [], []);
    Expect(noEdit.IsBasePassThrough
        && noEdit.HeaderBytes.SequenceEqual(fixture.Header)
        && noEdit.AssetBytes.SequenceEqual(fixture.Asset),
        "no-edit FX composition should preserve source bytes exactly");

    var replacement = CreateFxIndexedTexture(8, 4, 0x31);
    var addition = CreateFxIndexedTexture(4, 8, 0x73);
    var composed = FxTextureCatalog.Compose(
        GameId.UYA,
        fixture.Header,
        fixture.Asset,
        [new FxTextureReplacement(0, replacement)],
        [addition]);
    var inventory = FxTextureCatalog.Read(GameId.UYA, composed.HeaderBytes, composed.AssetBytes);
    Expect(!composed.IsBasePassThrough && inventory.Entries.Count == 3,
        "edited FX composition should append one deterministic trailing index");
    Expect(inventory.Entries.Select(value => value.Index).SequenceEqual([0, 1, 2]),
        "FX composition should preserve source indexes and assign contiguous appended indexes");
    Expect(PifMatches(inventory.Entries[0].CanonicalTextureBytes, replacement)
        && PifMatches(inventory.Entries[2].CanonicalTextureBytes, addition),
        "FX replacement and addition should semantically re-read with their exact indexed data");
    Expect(inventory.Entries[1].CanonicalTextureBytes.SequenceEqual(
            FxTextureCatalog.Read(GameId.UYA, fixture.Header, fixture.Asset).Entries[1].CanonicalTextureBytes),
        "FX composition should preserve unrelated source textures");
    var sourceModelOffset = BinaryPrimitives.ReadInt32LittleEndian(fixture.Header.AsSpan(0xe0));
    var outputModelOffset = BinaryPrimitives.ReadInt32LittleEndian(composed.HeaderBytes.AsSpan(0xe0));
    var outputFxBase = BinaryPrimitives.ReadInt32LittleEndian(composed.HeaderBytes.AsSpan(0x68));
    var outputPaletteOffset = BinaryPrimitives.ReadInt32LittleEndian(composed.HeaderBytes.AsSpan(0xc0));
    Expect(outputModelOffset > sourceModelOffset
        && outputFxBase + outputPaletteOffset < outputModelOffset
        && composed.AssetBytes.AsSpan(0, sourceModelOffset).SequenceEqual(fixture.Asset.AsSpan(0, sourceModelOffset))
        && composed.AssetBytes.AsSpan(outputModelOffset, 0x20)
            .SequenceEqual(fixture.Asset.AsSpan(sourceModelOffset, 0x20)),
        "FX composition should expand before the model heap and relocate downstream model data unchanged");
    Expect(BinaryPrimitives.ReadInt32LittleEndian(composed.HeaderBytes.AsSpan(0x7c)) == composed.AssetBytes.Length
        && BinaryPrimitives.ReadInt32LittleEndian(composed.HeaderBytes.AsSpan(0x8c)) == composed.AssetBytes.Length,
        "UYA FX composition should extend the runtime-loaded asset boundary over appended texture data");
    Expect(inventory.Entries[0].PaletteOffset != inventory.Entries[1].PaletteOffset
        && inventory.Entries[0].PaletteOffset != inventory.Entries[2].PaletteOffset,
        "FX edits should own private appended palette storage");

    var splitFixture = CreateFxTextureFixture(includeInterveningPayload: true);
    var splitComposition = FxTextureCatalog.Compose(
        GameId.UYA,
        splitFixture.Header,
        splitFixture.Asset,
        [new FxTextureReplacement(0, replacement)],
        []);
    var splitDefinitionsOffset = BinaryPrimitives.ReadInt32LittleEndian(
        splitComposition.HeaderBytes.AsSpan(0x5c));
    var splitFxBase = BinaryPrimitives.ReadInt32LittleEndian(splitComposition.HeaderBytes.AsSpan(0x68));
    var splitPaletteOffset = BinaryPrimitives.ReadInt32LittleEndian(
        splitComposition.HeaderBytes.AsSpan(splitDefinitionsOffset));
    var sourceHeightmapOffset = BinaryPrimitives.ReadInt32LittleEndian(splitFixture.Header.AsSpan(0xa4));
    var outputHeightmapOffset = BinaryPrimitives.ReadInt32LittleEndian(splitComposition.HeaderBytes.AsSpan(0xa4));
    Expect(splitFxBase + splitPaletteOffset == sourceHeightmapOffset
        && outputHeightmapOffset > sourceHeightmapOffset
        && splitComposition.AssetBytes.AsSpan(outputHeightmapOffset, 0x20)
            .SequenceEqual(splitFixture.Asset.AsSpan(sourceHeightmapOffset, 0x20)),
        "FX composition should append at the next native payload boundary without duplicating an intervening payload");

    var unalignedData = CreateFxTextureFixture(0x120);
    var unalignedComposition = FxTextureCatalog.Compose(
        GameId.UYA,
        unalignedData.Header,
        unalignedData.Asset,
        [new FxTextureReplacement(0, replacement)],
        []);
    Expect(PifMatches(
            FxTextureCatalog.Read(
                GameId.UYA, unalignedComposition.HeaderBytes, unalignedComposition.AssetBytes).Entries[0]
                .CanonicalTextureBytes,
            replacement),
        "FX composition should align appended storage relative to an unaligned FX data base");

    var compressed = WadCompression.CompressVerified(fixture.Asset).CompressedBytes;
    var compressedComposition = FxTextureCatalog.Compose(
        GameId.UYA,
        fixture.Header,
        compressed,
        [new FxTextureReplacement(1, replacement)],
        []);
    Expect(BinaryMagic.IsWad(compressedComposition.AssetBytes)
        && FxTextureCatalog.Read(GameId.UYA, compressedComposition.HeaderBytes, compressedComposition.AssetBytes)
            .Entries[1].Width == replacement.Width,
        "FX composition should retain compressed storage and verify the recompressed payload");

    ExpectThrows<InvalidDataException>(() => FxTextureCatalog.Compose(
        GameId.UYA, fixture.Header, fixture.Asset,
        [new(0, replacement), new(0, replacement)], []));
    ExpectThrows<InvalidDataException>(() => FxTextureCatalog.Compose(
        GameId.UYA, fixture.Header, fixture.Asset, [new(2, replacement)], []));
    ExpectThrows<InvalidDataException>(() => FxTextureCatalog.Compose(
        GameId.UYA, fixture.Header, fixture.Asset, [], [new(3, 4, new byte[0x400], new byte[12])]));
    var trailingData = fixture.Header.ToArray();
    WriteInt32(trailingData, 0x7c, fixture.Asset.Length - 0x10);
    ExpectThrows<InvalidDataException>(() => FxTextureCatalog.Compose(
        GameId.UYA, trailingData, fixture.Asset, [new(0, replacement)], []));
    ExpectThrows<NotSupportedException>(() => FxTextureCatalog.Compose(
        GameId.DL, fixture.Header, fixture.Asset, [], []));
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    ExpectThrows<OperationCanceledException>(() => FxTextureCatalog.Compose(
        GameId.UYA, fixture.Header, fixture.Asset, [], [addition], cancellationToken: cancellation.Token));
}

static FxIndexedTexture CreateFxIndexedTexture(int width, int height, byte marker) => new(
    width,
    height,
    Enumerable.Range(0, FxTextureInventoryReader.PaletteLength)
        .Select(index => unchecked((byte)(marker + index))).ToArray(),
    Enumerable.Range(0, checked(width * height))
        .Select(index => unchecked((byte)(marker ^ index))).ToArray());

static bool PifMatches(byte[] bytes, FxIndexedTexture expected)
{
    var pif = PifReader.Read(bytes);
    return pif.Header.USize == expected.Width
        && pif.Header.VSize == expected.Height
        && pif.PaletteData.AsSpan().SequenceEqual(expected.PaletteBytes.Span)
        && pif.PixelData.AsSpan().SequenceEqual(expected.PixelBytes.Span);
}

static (byte[] Header, byte[] Asset) CreateFxTextureFixture(
    int dataOffset = 0x100,
    bool includeInterveningPayload = false)
{
    var header = new byte[0x100];
    WriteInt32(header, 0x18, 1);
    WriteInt32(header, 0x1c, 0xe0);
    WriteInt32(header, 0x58, 2);
    WriteInt32(header, 0x5c, 0xc0);
    WriteInt32(header, 0x68, dataOffset);
    WriteInt32(header, 0xc0, 0);
    WriteInt32(header, 0xc4, 0x400);
    WriteInt32(header, 0xc8, 4);
    WriteInt32(header, 0xcc, 4);
    WriteInt32(header, 0xd0, 0x500);
    WriteInt32(header, 0xd4, 0x900);
    WriteInt32(header, 0xd8, 4);
    WriteInt32(header, 0xdc, 4);
    var fxEnd = checked(dataOffset + 0xa00);
    var modelOffset = checked(fxEnd + (includeInterveningPayload ? 0x20 : 0));
    if (includeInterveningPayload) WriteInt32(header, 0xa4, fxEnd);
    WriteInt32(header, 0xe0, modelOffset);
    WriteInt32(header, 0xe4, 1);
    var asset = new byte[checked(modelOffset + 0x20)];
    for (var index = 0; index < asset.Length; index++) asset[index] = unchecked((byte)index);
    WriteInt32(header, 0x7c, asset.Length);
    WriteInt32(header, 0x8c, asset.Length);
    return (header, asset);
}

static void ValidateFxTextureCorpusWhenAvailable()
{
    var root = Path.Combine("test-assets", "extractions_uya");
    if (!Directory.Exists(root)) return;
    foreach (var assets in Directory.EnumerateDirectories(root, "assets", SearchOption.AllDirectories)
                 .Order(StringComparer.Ordinal))
    {
        var headerPath = Path.Combine(assets, "asset_header.bin");
        var assetPath = Path.Combine(assets, "asset_wad.bin");
        if (!File.Exists(headerPath) || !File.Exists(assetPath)) continue;
        var header = File.ReadAllBytes(headerPath);
        var asset = File.ReadAllBytes(assetPath);
        var inventory = FxTextureCatalog.Read(GameId.UYA, header, asset);
        var expectedCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0x58));
        var fixtureName = Path.GetFileName(Path.GetDirectoryName(assets));
        Expect(inventory.Entries.Count == expectedCount && inventory.Entries.All(entry => entry.IsValid),
            $"UYA FX fixture {fixtureName} should expose every validated source definition");
        Expect(inventory.Entries.Select(entry => entry.Index).SequenceEqual(Enumerable.Range(0, expectedCount)),
            $"UYA FX fixture {fixtureName} should preserve stable source ordering");

        if (inventory.Entries.Count == 0) continue;
        var sourceHeader = LevelAssetReader.ReadHeader(header);
        var sourceFxEnd = sourceHeader.FxTextureDataOffset + inventory.Entries.Max(entry =>
            Math.Max(entry.PaletteOffset + entry.PaletteLength, entry.PixelOffset + entry.PixelLength));
        if (sourceFxEnd != sourceHeader.HeightmapOffset) continue;
        var sourceTexture = PifReader.Read(inventory.Entries[0].CanonicalTextureBytes);
        var composition = FxTextureCatalog.Compose(
            GameId.UYA,
            header,
            asset,
            [new(0, new(
                sourceTexture.Header.USize,
                sourceTexture.Header.VSize,
                sourceTexture.PaletteData,
                sourceTexture.PixelData))],
            []);
        var definitionsOffset = BinaryPrimitives.ReadInt32LittleEndian(composition.HeaderBytes.AsSpan(0x5c));
        var replacementPaletteOffset = BinaryPrimitives.ReadInt32LittleEndian(
            composition.HeaderBytes.AsSpan(definitionsOffset));
        var outputHeader = LevelAssetReader.ReadHeader(composition.HeaderBytes);
        Expect(outputHeader.FxTextureDataOffset + replacementPaletteOffset == sourceHeader.HeightmapOffset
            && outputHeader.HeightmapOffset > sourceHeader.HeightmapOffset,
            $"UYA FX fixture {fixtureName} should expand only the native FX segment");
    }
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

    var defaultZeroOffsetSlice = LevelAssetReader.ReadAssetSlice(assetData, 0, knownOffsets);
    Expect(defaultZeroOffsetSlice.Length == 0, "asset offset zero should be treated as absent by default");

    var tfragSlice = LevelAssetReader.ReadAssetSlice(assetData, 0, knownOffsets, allowZeroOffset: true);
    Expect(tfragSlice.SequenceEqual(assetData[..10]), "tfrag asset slices should allow offset zero and stop at the next known asset offset");

    var nonZeroSlice = LevelAssetReader.ReadAssetSlice(assetData, 10, knownOffsets);
    Expect(nonZeroSlice.SequenceEqual(assetData[10..20]), "non-zero asset slices should stop at the next known asset offset");

    var headerBytes = new byte[0xc0];
    WriteInt32(headerBytes, 0x10, 0x100);
    WriteInt32(headerBytes, 0x14, 0x1000);
    WriteInt32(headerBytes, 0x78, 0x200);
    var header = LevelAssetReader.ReadHeader(headerBytes);
    var gcOffsets = LevelAssetReader.CollectKnownAssetOffsets(header, 0x2000, [], [], []);
    var dlOffsets = LevelAssetReader.CollectKnownAssetOffsets(
        header, 0x2000, [], [], [], [header.LightCuboidsOffset]);
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

    var classIds = LevelAssetReader.ReadMobyGsStashClassIds(headerBytes, 0xf0);
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
    var definition = LevelAssetReader.ReadTextureDefinitions(headerBytes, 0xe0, 1).Single();
    var unswizzledPng = LevelAssetReader.BuildAssetTexture(
        "moby",
        0,
        definition,
        palette,
        assetBytes,
        textureDataOffset: 0,
        isSwizzled: false).PngBytes;
    var swizzledPng = LevelAssetReader.BuildAssetTexture(
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

    var definition = new LevelAssetTextureDefinition(
        Index: 7,
        TextureOffset: 0x20,
        Width: 4,
        Height: 4,
        Type: 3,
        PaletteId: 0,
        MipmapPaletteId: 1,
        Padding: 0);

    var texture = LevelAssetReader.BuildAssetTexture(
        "moby",
        0,
        definition,
        palette,
        assetData,
        textureDataOffset: 0x40);

    var pif = PifReader.Read(texture.PifBytes);
    Expect(pif.TotalMipLevels == 3, "DL normalized asset texture should store base mip plus mipmaps in PIF");
    Expect(texture.PngBytes.Length > 0, "DL normalized asset texture should generate a PNG preview");
    Expect(texture.Metadata.SourceDefinition is LevelAssetTextureDefinition, "texture manifest metadata should retain source table definition");
    Expect(texture.Metadata.MipPixelOffsets.SequenceEqual([0x70, 0x100]), "texture manifest metadata should retain mip source offsets");

    var type2Texture = PifReader.Read(LevelAssetReader.BuildAssetTexture(
        "moby",
        0,
        definition with { Type = 2, MipmapPaletteId = -1 },
        palette,
        assetData,
        textureDataOffset: 0x40).PifBytes);
    Expect(type2Texture.PixelData.SequenceEqual(assetData.AsSpan(0x60, 16).ToArray())
        && type2Texture.MipPixelData.Single().SequenceEqual(assetData.AsSpan(0x70, 4).ToArray()),
        "type 2 asset textures should read their base pixels and first mip from asset data");

    var overlappingPaletteData = new byte[0x500];
    for (var i = 0; i < overlappingPaletteData.Length; i++)
    {
        overlappingPaletteData[i] = (byte)(i & 0xff);
    }

    var paletteStrideTexture = LevelAssetReader.BuildAssetTexture(
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

static byte ReadFirstAccessorByte(JsonElement gltf, byte[] buffer, int accessorIndex)
{
    var accessor = gltf.GetProperty("accessors")[accessorIndex];
    var view = gltf.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
    var offset = view.GetProperty("byteOffset").GetInt32()
        + (accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0);
    return buffer[offset];
}

static uint[] ReadAccessorUInt32Values(JsonElement gltf, byte[] buffer, int accessorIndex)
{
    var accessor = gltf.GetProperty("accessors")[accessorIndex];
    var view = gltf.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
    var offset = view.GetProperty("byteOffset").GetInt32()
        + (accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0);
    return Enumerable.Range(0, accessor.GetProperty("count").GetInt32())
        .Select(index => BinaryPrimitives.ReadUInt32LittleEndian(
            buffer.AsSpan(offset + index * sizeof(uint), sizeof(uint))))
        .ToArray();
}

static byte[] CreateSyntheticUyaCollision()
{
    var data = new byte[0x100];
    WriteInt32(data, 0x00, 0x40);
    WriteInt32(data, 0x04, 0xc0);

    WriteInt16(data, 0x40, 0);
    WriteUInt16(data, 0x42, 1);
    WriteUInt16(data, 0x44, 2);
    WriteInt16(data, 0x48, 0);
    WriteUInt16(data, 0x4a, 1);
    WriteUInt32(data, 0x4c, 0x10);
    WriteInt16(data, 0x50, 0);
    WriteUInt16(data, 0x52, 2);
    WriteUInt32(data, 0x54, 0x2003);
    WriteUInt32(data, 0x58, 0x5002);

    WriteUInt16(data, 0x60, 2);
    data[0x62] = 7;
    data[0x63] = 1;
    var firstVertices = new[]
    {
        (-64, -64, 0),
        (0, -64, 0),
        (0, 0, 0),
        (-64, 0, 0),
        (96, -64, -64),
        (160, -64, -64),
        (128, 0, -64),
    };
    for (var index = 0; index < firstVertices.Length; index++)
    {
        var vertex = firstVertices[index];
        WriteUInt32(data, 0x64 + index * 4, PackCollisionVertex(vertex.Item1, vertex.Item2, vertex.Item3));
    }

    data[0x80] = 0;
    data[0x81] = 1;
    data[0x82] = 2;
    data[0x83] = 0x21;
    data[0x84] = 4;
    data[0x85] = 5;
    data[0x86] = 6;
    data[0x87] = 0xab;
    data[0x88] = 3;

    WriteUInt16(data, 0x90, 1);
    data[0x92] = 3;
    var secondVertices = new[]
    {
        (-160, -64, -64),
        (-96, -64, -64),
        (-128, 0, -64),
    };
    for (var index = 0; index < secondVertices.Length; index++)
    {
        var vertex = secondVertices[index];
        WriteUInt32(data, 0x94 + index * 4, PackCollisionVertex(vertex.Item1, vertex.Item2, vertex.Item3));
    }

    data[0xa0] = 0;
    data[0xa1] = 1;
    data[0xa2] = 2;
    data[0xa3] = 0xab;

    WriteInt32(data, 0xc0, 1);
    WriteUInt16(data, 0xd0, 64);
    WriteUInt16(data, 0xd2, 64);
    WriteUInt16(data, 0xd4, 64);
    WriteUInt16(data, 0xd6, 128);
    WriteUInt16(data, 0xd8, 1);
    WriteUInt16(data, 0xda, 3);
    WriteUInt32(data, 0xdc, 0x20);
    var barrierVertices = new[]
    {
        (64, 64, 64),
        (128, 64, 64),
        (64, 128, 64),
    };
    for (var index = 0; index < barrierVertices.Length; index++)
    {
        var vertex = barrierVertices[index];
        WriteUInt16(data, 0xe0 + index * 8, (ushort)vertex.Item1);
        WriteUInt16(data, 0xe2 + index * 8, (ushort)vertex.Item2);
        WriteUInt16(data, 0xe4 + index * 8, (ushort)vertex.Item3);
    }

    data[0xf8] = 0;
    data[0xf9] = 1;
    data[0xfa] = 2;
    return data;
}

static uint PackCollisionVertex(int x64, int y64, int z64) =>
    (uint)(((x64 / 4) & 0x3ff) | (((y64 / 4) & 0x3ff) << 10) | ((z64 & 0xfff) << 20));


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
