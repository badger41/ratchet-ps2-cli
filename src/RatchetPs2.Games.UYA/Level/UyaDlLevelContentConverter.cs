using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Fx;
using RatchetPs2.Core.Gameplay;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.DL.Gameplay;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.UYA.Builders;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaDlLevelContentConverter
{
    public static LevelContentConversion Convert(byte[] levelWad, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(levelWad);
        cancellationToken.ThrowIfCancellationRequested();
        var package = DlLevelWadUnpacker.Unpack(levelWad);
        byte[] Find(string path) => package.Files.SingleOrDefault(file => file.Path == path)?.Bytes ?? [];
        var raw = Find("gameplay/gameplay_core.bin");
        if (raw.Length == 0) throw new InvalidDataException("DL donor core gameplay is missing.");
        var blocks = GameplayLayoutReader.Read(raw, DlGameplayLayout.Core).Blocks.ToDictionary(block => block.SemanticName, block => block.PayloadBytes);
        foreach (var (native, path) in new[] {
            ("directional_lights", "world/lighting/directional_lights.bin"),
            ("tie_classes", "world/tie/class_ids.bin"), ("tie_instances", "world/tie/instances.bin"),
            ("tie_groups", "world/tie/groups.bin"), ("shrub_classes", "world/shrub/class_ids.bin"),
            ("shrub_instances", "world/shrub/instances.bin"), ("shrub_groups", "world/shrub/groups.bin"),
            ("occlusion", "world/occlusion/instance_mapping.bin"), ("tie_ambient_rgbas", "world/tie/colors.bin") })
            blocks[native] = Find(path);
        var present = blocks.Where(pair => pair.Value.Length > 0).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        byte[] Payload(string name) => blocks.GetValueOrDefault(name) ?? [];
        foreach (var name in new[] { "tie_instances", "shrub_instances", "moby_instances" })
            if (Payload(name).Length == 0) blocks[name] = new byte[0x10];
        blocks["moby_instances"] = ConvertMobys(Payload("moby_instances"), cancellationToken);
        // Empty DL fixup slots can be a single aligned zero block, not a (PVar 0, offset 0) fixup.
        foreach (var name in new[] { "pvar_moby_links", "pvar_relative_pointers" })
            if (Payload(name).Length == 0x10 && !Payload(name).AsSpan().ContainsAnyExcept((byte)0)
                && (Payload("pvar_table").Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(Payload("pvar_table").AsSpan(4)) < 4))
                blocks[name] = [];

        if (Payload("level_settings").Length > 0)
        {
            _ = DlLevelSettingsReader.Read(Payload("level_settings"));
            var settings = new byte[UyaLevelSettingsReader.MinimumSize];
            Payload("level_settings").AsSpan(0, 0x5c).CopyTo(settings);
            blocks["level_settings"] = settings;
        }
        // Keep DL behavior/environment records in RawGameplay until their UYA equivalents are mapped.
        foreach (var name in new[] { "cameras", "sound_instances", "point_lights", "env_sample_points", "camera_collision_grid" }) blocks[name] = [];
        var headerBytes = Find("assets/asset_header.bin").ToArray();
        var header = LevelAssetReader.ReadHeader(headerBytes);
        var storedAssets = Find("assets/asset_wad.bin");
        var assets = BinaryMagic.IsWad(storedAssets)
            ? WadCompression.Decompress(storedAssets, new(), cancellationToken) : storedAssets.ToArray();
        var mobys = LevelAssetReader.ReadModelDefinitions(headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = LevelAssetReader.ReadModelDefinitions(headerBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = LevelAssetReader.ReadShrubDefinitions(headerBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var offsets = LevelAssetReader.CollectKnownAssetOffsets(header, assets.Length, mobys, ties, shrubs, [header.SceneViewSize]);
        var sky = LevelAssetReader.ReadAssetSlice(assets, header.SkyOffset, offsets);
        if (sky.Length > 0)
            UyaSkyboxConverter.ConvertFromDl(sky, package.LevelWad.Level, cancellationToken).CopyTo(assets, header.SkyOffset);
        var terrain = LevelAssetReader.ReadAssetSlice(assets, header.TerrainOffset, offsets, allowZeroOffset: true);
        var terrainCount = terrain.Length > 0 && terrain.AsSpan().ContainsAnyExcept((byte)0)
            ? TfragTerrainReader.Read(terrain).TotalTfragCount : 0;
        if (terrainCount is < 0 or > 100_000) throw new InvalidDataException("DL terrain mapping count exceeds the item limit.");
        var mappingBytes = Payload("occlusion");
        if (mappingBytes.Length > 0)
        {
            var tieBytes = Payload("tie_instances");
            var tiesForMapping = tieBytes.Length == 0 ? [] : UyaTieInstancesReader.Read(tieBytes).Instances;
            blocks["occlusion"] = UyaOcclusionMappingsWriter.NormalizeReplacement(mappingBytes, terrainCount, tiesForMapping);
        }
        var palette = Find("assets/palette.bin");
        var terrainTextures = LevelAssetReader.ReadTextureDefinitions(headerBytes, header.TerrainTextureOffset, header.TerrainTextureCount)
            .Select(definition =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var texture = LevelAssetReader.BuildAssetTexture("tfrag", definition.Index, definition, palette,
                    assets, header.TextureDataOffset, isSwizzled: true).PifBytes;
                return UyaLevelAssetModelConverter.ConvertDlTexture(texture);
            }).ToArray();
        // DL FX images smaller than a swizzle block are linear; larger images need unswizzling.
        foreach (var texture in FxTextureInventoryReader.Read(headerBytes, assets, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!texture.IsValid) continue;
            var pif = PifWriter.Write(PifWriter.CreateIndexed8(texture.Width, texture.Height,
                texture.PaletteBytes, texture.PixelBytes, isSwizzled: texture.Width >= 16 && texture.Height >= 4));
            PifReader.Read(UyaLevelAssetModelConverter.ConvertDlTexture(pif)).PixelData.CopyTo(assets, checked(header.FxTextureDataOffset + texture.PixelOffset));
        }
        var files = UyaConvertedLevelPackageBuilder.Gameplay(blocks).ToList();
        files.AddRange(UyaConvertedLevelPackageBuilder.LevelData(headerBytes, palette, assets, Find));
        var nativeHeader = new byte[UyaLevelConstants.LevelWadHeaderSize];
        Write(nativeHeader, 0, nativeHeader.Length); Write(nativeHeader, 8, package.LevelWad.Level);
        files.Add(new("level_wad/header.bin", nativeHeader, "application/octet-stream"));
        // DL stores this mapping with the world instances; UYA also keeps an external copy.
        if (Payload("occlusion").Length > 0) files.Add(new("occlusion/occlusion.bin", Payload("occlusion"), "application/octet-stream"));
        for (var index = 1; index < package.LevelWad.Chunks.Count; index++)
        {
            var bytes = Find($"level_wad/chunks/chunk{index}.wad");
            if (bytes.Length > 0) files.Add(new($"level_wad/chunks/chunk{index}.wad", bytes, "application/octet-stream"));
        }
        var models = mobys.Where(model => model.ModelOffset > 0).Select(model => new LevelContentModelUse(FrontendAssetKind.Moby, model.ModelId, model.Index))
            .Concat(ties.Where(model => model.ModelOffset > 0).Select(model => new LevelContentModelUse(FrontendAssetKind.Tie, model.ModelId, model.Index)))
            .Concat(shrubs.Where(model => model.ModelOffset > 0).Select(model => new LevelContentModelUse(FrontendAssetKind.Shrub, model.ModelId, model.Index))).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new(files, raw, new Dictionary<int, Vector3>(), package.LevelWad.Chunks.Count, models, terrainTextures, present);
    }

    private static byte[] ConvertMobys(byte[] bytes, CancellationToken token)
    {
        var source = DlMobyInstancesReader.Read(bytes);
        var output = new byte[checked(0x10 + source.StaticCount * UyaMobyInstancesReader.RecordSize)];
        bytes.AsSpan(0, 0x10).CopyTo(output);
        for (var index = 0; index < source.StaticCount; index++)
        {
            token.ThrowIfCancellationRequested();
            var input = bytes.AsSpan(0x10 + index * DlMobyInstancesReader.RecordSize, DlMobyInstancesReader.RecordSize);
            if (BinaryPrimitives.ReadInt32LittleEndian(input) != DlMobyInstancesReader.RecordSize)
                throw new InvalidDataException("DL Moby instance has an invalid record size.");
            var target = output.AsSpan(0x10 + index * UyaMobyInstancesReader.RecordSize, UyaMobyInstancesReader.RecordSize);
            input[..8].CopyTo(target);
            input.Slice(8, 8).CopyTo(target[0x10..]);
            input[0x10..].CopyTo(target[0x28..]);
            Write(target, 0, UyaMobyInstancesReader.RecordSize);
        }
        return output;
    }

    private static void Write(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], value);
}
