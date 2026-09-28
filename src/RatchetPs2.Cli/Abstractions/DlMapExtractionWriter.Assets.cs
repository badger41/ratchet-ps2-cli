using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Games.DL.Level;
namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private static void ExtractAssets(
        string outputDirectory,
        int levelIndex,
        byte[] headerBytes,
        byte[] paletteBytes,
        byte[] assetBytes,
        IDictionary<string, object?> rootManifest)
    {
        CleanLegacyAssetRootArtifacts(outputDirectory);

        var header = LevelAssetReader.ReadHeader(headerBytes);
        var textureMetadata = new List<LevelAssetTextureMetadata>();
        var omittedRawPayloads = new List<object>
        {
            new
            {
                Path = "asset_header.bin",
                Replacement = "header/fixed.bin + header/tables/*.bin + named header-derived payload files + manifest parsed metadata",
                Reason = "The aggregate asset header is split into named rebuild inputs instead of kept as one root-level blob."
            },
            new
            {
                Path = "palette.bin",
                Replacement = "normalized texture PIF artifacts plus texture manifest metadata",
                Reason = "Palette bytes are represented by normalized texture PIF artifacts and repack metadata."
            },
            new
            {
                Path = "asset_wad.bin",
                Replacement = "sliced asset files, normalized texture PIF artifacts, unknown/range_*.bin when present, and AssetWadCoverage",
                Reason = "The aggregate asset WAD is represented by rebuildable component files and coverage metadata."
            },
            new
            {
                Path = "particle_def.bin",
                Replacement = "particle/definitions.bin",
                Reason = "Particle definition bytes are still preserved, but grouped with particle assets."
            },
            new
            {
                Path = "sound_remap.bin",
                Replacement = "sound/remap.bin",
                Reason = "Sound remap bytes are still preserved, but grouped with sound assets."
            },
            new
            {
                Path = "moby_sound_remap.bin",
                Replacement = "moby/sound_remap.bin",
                Reason = "Moby sound remap bytes are still preserved, but grouped with moby assets."
            },
            new
            {
                Path = "moby_gs_stash_list.bin",
                Replacement = "moby/gs_stash_list.bin",
                Reason = "Moby GS stash list bytes are still preserved, but grouped with moby assets."
            }
        };
        var assetManifest = new Dictionary<string, object?>
        {
            ["Header"] = header,
            ["HeaderLength"] = headerBytes.Length,
            ["OmittedRawPayloads"] = omittedRawPayloads
        };
        var headerArtifacts = WriteAssetHeaderArtifacts(outputDirectory, headerBytes, header);

        var allMipmapDefinitions = LevelAssetReader.ReadMipmapDefinitions(
            headerBytes,
            header.GsRamOffset,
            Math.Max(0, header.GsRamCount + header.ExtraMipmapCount));
        var gsStashDefinitions = allMipmapDefinitions.Skip(header.GsRamCount).ToArray();
        var mobyGsStashClassIds = LevelAssetReader.ReadMobyGsStashClassIds(
            headerBytes,
            header.MobyGsStashListOffset);
        var mobyDefinitions = LevelAssetReader.ReadModelDefinitions(headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var tieDefinitions = LevelAssetReader.ReadModelDefinitions(headerBytes, header.TieModelOffset, header.TieModelCount);
        var shrubDefinitions = LevelAssetReader.ReadShrubDefinitions(headerBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var tfragTextureDefinitions = LevelAssetReader.ReadTextureDefinitions(headerBytes, header.TerrainTextureOffset, header.TerrainTextureCount);
        var mobyTextureDefinitions = LevelAssetReader.ReadTextureDefinitions(headerBytes, header.MobyTextureOffset, header.MobyTextureCount);
        var tieTextureDefinitions = LevelAssetReader.ReadTextureDefinitions(headerBytes, header.TieTextureOffset, header.TieTextureCount);
        var shrubTextureDefinitions = LevelAssetReader.ReadTextureDefinitions(headerBytes, header.ShrubTextureOffset, header.ShrubTextureCount);
        var particleDefinitions = LevelAssetReader.ReadParticleTextureDefinitions(
            headerBytes,
            header.ParticleTextureDefOffset,
            header.ParticleTextureCount);
        var fxDefinitions = LevelAssetReader.ReadFxTextureDefinitions(
            headerBytes,
            header.FxTextureDefOffset,
            header.FxTextureCount);
        assetManifest["HeaderTables"] = new
        {
            MipmapDefinitions = allMipmapDefinitions,
            MobyGsStashClassIds = mobyGsStashClassIds,
            MobyDefinitions = mobyDefinitions,
            TieDefinitions = tieDefinitions,
            ShrubDefinitions = shrubDefinitions,
            TfragTextureDefinitions = tfragTextureDefinitions,
            MobyTextureDefinitions = mobyTextureDefinitions,
            TieTextureDefinitions = tieTextureDefinitions,
            ShrubTextureDefinitions = shrubTextureDefinitions,
            ParticleTextureDefinitions = particleDefinitions,
            FxTextureDefinitions = fxDefinitions
        };

        var knownAssetOffsets = LevelAssetReader.CollectKnownAssetOffsets(
            header,
            assetBytes.Length,
            mobyDefinitions,
            tieDefinitions,
            shrubDefinitions,
            [header.LightCuboidsOffset]);

        ExtractTfrag(outputDirectory, header, tfragTextureDefinitions, paletteBytes, assetBytes, textureMetadata, knownAssetOffsets);
        ExtractModelFamily(
            CreateDirectory(outputDirectory, "moby"),
            "moby",
            mobyDefinitions,
            mobyTextureDefinitions,
            paletteBytes,
            assetBytes,
            header.TextureDataOffset,
            gsStashDefinitions,
            mobyGsStashClassIds,
            textureMetadata,
            knownAssetOffsets);
        ExtractModelFamily(
            CreateDirectory(outputDirectory, "tie"),
            "tie",
            tieDefinitions,
            tieTextureDefinitions,
            paletteBytes,
            assetBytes,
            header.TextureDataOffset,
            gsStashDefinitions: [],
            unswizzledModelIds: [],
            textureMetadata,
            knownAssetOffsets);
        ExtractShrubFamily(
            CreateDirectory(outputDirectory, "shrub"),
            shrubDefinitions,
            shrubTextureDefinitions,
            paletteBytes,
            assetBytes,
            header.TextureDataOffset,
            textureMetadata,
            knownAssetOffsets);
        var headerPayloads = ExtractLooseAssetBlocks(outputDirectory, header, headerBytes, assetBytes, knownAssetOffsets);
        ExtractParticleAndFxTextures(outputDirectory, header, particleDefinitions, fxDefinitions, assetBytes, textureMetadata);
        ExtractGsSpecialTextures(outputDirectory, header, paletteBytes, gsStashDefinitions, textureMetadata);
        var gltfExports = ExportAssetGltfs(
            outputDirectory,
            levelIndex,
            mobyDefinitions,
            tieDefinitions,
            shrubDefinitions);

        var assetWadCoverage = WriteAssetWadCoverageArtifacts(
            outputDirectory,
            header,
            assetBytes,
            knownAssetOffsets,
            mobyDefinitions,
            tieDefinitions,
            shrubDefinitions,
            textureMetadata);

        AddOmittedRawPayload(
            rootManifest,
            "assets/asset_wad.bin",
            "assets/manifest.json AssetWadCoverage + sliced asset files + normalized texture PIF artifacts",
            "The aggregate asset WAD is represented by rebuildable component files and coverage metadata.");
        AddOmittedRawPayload(
            rootManifest,
            "assets/asset_header.bin",
            "assets/header/fixed.bin + assets/header/tables/*.bin + assets/manifest.json Header/HeaderTables/HeaderPayloads",
            "The aggregate asset header is split into named rebuild inputs and parsed metadata.");

        assetManifest["HeaderArtifacts"] = headerArtifacts;
        assetManifest["HeaderPayloads"] = headerPayloads;
        assetManifest["AssetWadCoverage"] = assetWadCoverage;
        assetManifest["GltfExports"] = gltfExports;
        assetManifest["GltfExportCount"] = gltfExports.Count(export => export.Status == "written");
        assetManifest["GltfExportFailureCount"] = gltfExports.Count(export => export.Status == "error");
        assetManifest["Textures"] = textureMetadata;
        assetManifest["TextureCount"] = textureMetadata.Count;
        WriteJson(Path.Combine(outputDirectory, "manifest.json"), assetManifest);
        rootManifest["AssetHeader"] = header;
        rootManifest["AssetWadCoverage"] = assetWadCoverage;
        rootManifest["TextureCount"] = textureMetadata.Count;
    }

    private static void ExtractTfrag(
        string outputDirectory,
        LevelAssetHeader header,
        IReadOnlyList<LevelAssetTextureDefinition> textureDefinitions,
        byte[] paletteBytes,
        byte[] assetBytes,
        List<LevelAssetTextureMetadata> textureMetadata,
        IReadOnlyList<int> knownAssetOffsets)
    {
        CleanLegacyTerrainArtifacts(outputDirectory);

        var tfragDirectory = CreateDirectory(outputDirectory, "tfrag");
        CleanOldTfragArtifacts(tfragDirectory);
        File.WriteAllBytes(
            Path.Combine(tfragDirectory, "tfrag.bin"),
            LevelAssetReader.ReadAssetSlice(
                assetBytes,
                header.TerrainOffset,
                knownAssetOffsets,
                allowZeroOffset: true));

        var texturesDirectory = CreateDirectory(tfragDirectory, "textures");

        foreach (var textureDefinition in textureDefinitions)
        {
            WriteTexture(
                texturesDirectory,
                LevelAssetReader.BuildAssetTexture(
                    "tfrag",
                    textureDefinition.Index,
                    textureDefinition,
                    paletteBytes,
                    assetBytes,
                    header.TextureDataOffset),
                textureMetadata);
        }
    }

    private static void ExtractModelFamily(
        string outputDirectory,
        string family,
        IReadOnlyList<LevelAssetModelDefinition> modelDefinitions,
        IReadOnlyList<LevelAssetTextureDefinition> textureDefinitions,
        byte[] paletteBytes,
        byte[] assetBytes,
        int textureDataOffset,
        IReadOnlyList<LevelAssetMipmapDefinition> gsStashDefinitions,
        IReadOnlyList<int> unswizzledModelIds,
        List<LevelAssetTextureMetadata> textureMetadata,
        IReadOnlyList<int> knownAssetOffsets)
    {
        foreach (var modelDefinition in modelDefinitions)
        {
            var modelDirectory = CreateDirectory(outputDirectory, LevelAssetReader.GetAssetFolderName(modelDefinition.ModelId));
            if (!IsEmptyMobyModel(family, modelDefinition))
            {
                File.WriteAllBytes(
                    Path.Combine(modelDirectory, $"{family}.bin"),
                    LevelAssetReader.ReadAssetSlice(assetBytes, modelDefinition.ModelOffset, knownAssetOffsets));
            }

            WriteJson(Path.Combine(modelDirectory, $"{family}.json"), modelDefinition);

            var texturesDirectory = CreateDirectory(modelDirectory, "textures");
            var relativeTextureIndex = 0;
            var textureIsSwizzled = !unswizzledModelIds.Contains(modelDefinition.ModelId);
            foreach (var textureId in modelDefinition.TextureIds)
            {
                if (textureId == 0xff || textureId >= textureDefinitions.Count)
                {
                    continue;
                }

                WriteTexture(
                    texturesDirectory,
                    LevelAssetReader.BuildAssetTexture(
                        family,
                        relativeTextureIndex,
                        textureDefinitions[textureId],
                        paletteBytes,
                        assetBytes,
                        textureDataOffset,
                        gsStashDefinitions,
                        isSwizzled: textureIsSwizzled),
                    textureMetadata);
                relativeTextureIndex++;
            }
        }
    }

    private static bool IsEmptyMobyModel(string family, LevelAssetModelDefinition modelDefinition)
    {
        return family == "moby" && modelDefinition.ModelOffset == 0;
    }

    private static void ExtractShrubFamily(
        string outputDirectory,
        IReadOnlyList<LevelAssetShrubDefinition> shrubDefinitions,
        IReadOnlyList<LevelAssetTextureDefinition> textureDefinitions,
        byte[] paletteBytes,
        byte[] assetBytes,
        int textureDataOffset,
        List<LevelAssetTextureMetadata> textureMetadata,
        IReadOnlyList<int> knownAssetOffsets)
    {
        foreach (var shrubDefinition in shrubDefinitions)
        {
            var shrubDirectory = CreateDirectory(outputDirectory, LevelAssetReader.GetAssetFolderName(shrubDefinition.ModelId));
            File.WriteAllBytes(
                Path.Combine(shrubDirectory, "shrub.bin"),
                LevelAssetReader.ReadAssetSlice(assetBytes, shrubDefinition.ModelOffset, knownAssetOffsets));
            WriteJson(Path.Combine(shrubDirectory, "shrub.json"), shrubDefinition);

            var texturesDirectory = CreateDirectory(shrubDirectory, "textures");
            var relativeTextureIndex = 0;
            foreach (var textureId in shrubDefinition.TextureIds)
            {
                if (textureId == 0xff || textureId >= textureDefinitions.Count)
                {
                    continue;
                }

                WriteTexture(
                    texturesDirectory,
                    LevelAssetReader.BuildAssetTexture(
                        "shrub",
                        relativeTextureIndex,
                        textureDefinitions[textureId],
                        paletteBytes,
                        assetBytes,
                        textureDataOffset),
                    textureMetadata);
                relativeTextureIndex++;
            }

            if (shrubDefinition.Width > 0 && shrubDefinition.Height > 0 && shrubDefinition.TextureId > 0)
            {
                WriteTexture(
                    CreateDirectory(shrubDirectory, "billboard"),
                    LevelAssetReader.BuildShrubBillboardTexture(shrubDefinition, paletteBytes),
                    textureMetadata);
            }
        }
    }


    private static void ExtractParticleAndFxTextures(
        string outputDirectory,
        LevelAssetHeader header,
        IReadOnlyList<LevelParticleTextureDefinition> particleDefinitions,
        IReadOnlyList<LevelFxTextureDefinition> fxDefinitions,
        byte[] assetBytes,
        List<LevelAssetTextureMetadata> textureMetadata)
    {
        var particleDirectory = CreateDirectory(outputDirectory, "particle", "textures");
        foreach (var definition in particleDefinitions)
        {
            WriteTexture(
                particleDirectory,
                LevelAssetReader.BuildParticleTexture(definition, assetBytes, header.ParticleTextureDataOffset),
                textureMetadata);
        }

        var fxDirectory = CreateDirectory(outputDirectory, "fx", "textures");
        foreach (var definition in fxDefinitions)
        {
            WriteTexture(
                fxDirectory,
                LevelAssetReader.BuildFxTexture(definition, assetBytes, header.FxTextureDataOffset),
                textureMetadata);
        }
    }

    private static void ExtractGsSpecialTextures(
        string outputDirectory,
        LevelAssetHeader header,
        byte[] paletteBytes,
        IReadOnlyList<LevelAssetMipmapDefinition> gsStashDefinitions,
        List<LevelAssetTextureMetadata> textureMetadata)
    {
        var chromeDefinition = gsStashDefinitions.FirstOrDefault(item => item.Offset2 == header.ChromeTextureOffset);
        if (chromeDefinition is not null)
        {
            WriteTexture(
                CreateDirectory(outputDirectory, "chrome"),
                LevelAssetReader.BuildGsStashTexture(
                    "chrome",
                    0,
                    chromeDefinition,
                    header.ChromePaletteOffset,
                    paletteBytes),
                textureMetadata);
        }

        var glassDefinition = gsStashDefinitions.FirstOrDefault(item => item.Offset2 == header.GlassTextureOffset);
        if (glassDefinition is not null)
        {
            WriteTexture(
                CreateDirectory(outputDirectory, "glass"),
                LevelAssetReader.BuildGsStashTexture(
                    "glass",
                    0,
                    glassDefinition,
                    header.GlassPaletteOffset,
                    paletteBytes),
                textureMetadata);
        }
    }

    private static void WriteAssetBlock(
        string outputDirectory,
        string fileName,
        byte[] assetBytes,
        int offset,
        IReadOnlyList<int> knownAssetOffsets)
    {
        var bytes = LevelAssetReader.ReadAssetSlice(assetBytes, offset, knownAssetOffsets);
        if (bytes.Length > 0)
        {
            var outputPath = CombineRelativePath(outputDirectory, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, bytes);
            return;
        }

        DeleteIfExists(CombineRelativePath(outputDirectory, fileName));
    }

    private static void WriteTexture(
        string outputDirectory,
        LevelAssetTexture texture,
        List<LevelAssetTextureMetadata> textureMetadata)
    {
        var baseName = $"tex.{texture.Index:0000}";
        File.WriteAllBytes(Path.Combine(outputDirectory, $"{baseName}.pif"), texture.PifBytes);
        File.WriteAllBytes(Path.Combine(outputDirectory, $"{baseName}.png"), texture.PngBytes);
        textureMetadata.Add(texture.Metadata);
    }
}
