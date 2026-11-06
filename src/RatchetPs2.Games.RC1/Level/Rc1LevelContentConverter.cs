using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.RC1.Gameplay;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1LevelContentConverter
{
    public static LevelContentConversion ConvertToUya(byte[] levelWad, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(levelWad);
        cancellationToken.ThrowIfCancellationRequested();
        var package = Rc1LevelWadUnpacker.Unpack(levelWad);
        var source = Rc1LevelWadRenderPackageBuilder.GetAssetSources(package.Files);
        var headerBytes = source.HeaderBytes.ToArray();
        // These RC1 fields are unrelated to UYA's extra mipmaps and asset offsets.
        foreach (var offset in new[] { 0x84, 0xac, 0xb0, 0xb4, 0xb8 }) Write(headerBytes, offset, 0);
        var header = LevelAssetReader.ReadHeader(headerBytes);
        var assets = BinaryMagic.IsWad(source.AssetWadBytes)
            ? WadCompression.Decompress(source.AssetWadBytes, new(), cancellationToken) : source.AssetWadBytes.ToArray();
        var mobys = LevelAssetReader.ReadModelDefinitions(headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var ties = LevelAssetReader.ReadModelDefinitions(headerBytes, header.TieModelOffset, header.TieModelCount);
        var shrubs = LevelAssetReader.ReadShrubDefinitions(headerBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var offsets = LevelAssetReader.CollectKnownAssetOffsets(header, assets.Length, mobys, ties, shrubs, [header.SceneViewSize]);
        var sky = LevelAssetReader.ReadAssetSlice(assets, header.SkyOffset, offsets);
        if (sky.Length > 0) Rc1LevelAssetModelConverter.ConvertSky(sky, cancellationToken).CopyTo(assets, header.SkyOffset);
        var terrain = LevelAssetReader.ReadAssetSlice(assets, header.TerrainOffset, offsets, allowZeroOffset: true);
        var terrainCount = terrain.Length > 0 && terrain.AsSpan().ContainsAnyExcept((byte)0)
            ? TfragTerrainReader.Read(terrain).TotalTfragCount : 0;
        if (terrainCount is < 0 or > 100_000) throw new InvalidDataException("RC1 terrain mapping count exceeds the item limit.");

        var terrainDefinitions = LevelAssetReader.ReadTextureDefinitions(source.HeaderBytes, header.TerrainTextureOffset, header.TerrainTextureCount);
        var terrainTextures = terrainDefinitions.Select(definition =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var texture = LevelAssetReader.BuildAssetTexture("tfrag", definition.Index, definition, source.PaletteBytes,
                assets, header.TextureDataOffset, isSwizzled: false, useTextureFlags: false).PifBytes;
            var record = headerBytes.AsSpan(header.TerrainTextureOffset + definition.Index * 0x10);
            BinaryPrimitives.WriteInt16LittleEndian(record[8..], (short)(definition.Type >= 2 ? 3 : 1));
            if (definition.Type < 3) BinaryPrimitives.WriteInt16LittleEndian(record[12..], -1);
            return UyaLevelAssetModelConverter.ConvertLinearTexture(texture, RatchetPs2.Core.Textures.Palettes.TextureRole.Material);
        }).ToArray();

        var raw = package.Files.SingleOrDefault(file => file.Path == "gameplay/gameplay_core.bin")?.Bytes
            ?? throw new InvalidDataException("RC1 donor gameplay is missing.");
        var blocks = Rc1Gameplay.ReadCore(raw).Blocks.ToDictionary(block => block.SemanticName, block => block.PayloadBytes, StringComparer.Ordinal);
        var present = blocks.Where(pair => pair.Value.Length > 0).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        byte[] Payload(string name) => blocks.GetValueOrDefault(name) ?? [];
        var tieTable = Payload("tie_instances");
        if (tieTable.Length > 0)
        {
            var converted = Rc1Gameplay.ConvertTieInstances(tieTable);
            blocks["tie_instances"] = converted.Instances;
            var colors = UyaGameplayLightingReader.ReadTieAmbientRgbas(
                [.. converted.AmbientRgbas, 0xff, 0xff], BinaryPrimitives.ReadInt32LittleEndian(converted.Instances));
            // UYA adds alpha 128 itself; RC1's opaque RGB5 bit would saturate it to 255.
            blocks["tie_ambient_rgbas"] = UyaTieAmbientRgbasWriter.Write(colors.Select(value =>
            {
                var native = new byte[4 + value.Length];
                value.CopyTo(native, 4);
                for (var offset = 5; offset < native.Length; offset += 2) native[offset] &= 0x7f;
                return native;
            }).ToArray());
        }
        var mobyTable = Payload("moby_instances");
        if (mobyTable.Length > 0) blocks["moby_instances"] = ConvertMobys(mobyTable);
        var settings = Payload("level_settings");
        if (settings.Length > 0) blocks["level_settings"] = ConvertSettings(settings);
        var mappingBytes = Payload("occlusion");
        if (mappingBytes.Length > 0)
        {
            var tieBytes = Payload("tie_instances");
            var tiesForMapping = tieBytes.Length == 0 ? [] : UyaTieInstancesReader.Read(tieBytes).Instances;
            blocks["occlusion"] = UyaOcclusionMappingsWriter.NormalizeReplacement(mappingBytes, terrainCount, tiesForMapping);
        }
        // Keep behavior records in RawGameplay until their UYA equivalents are mapped.
        foreach (var name in new[] { "cameras", "sound_instances", "point_lights", "env_transitions", "env_sample_points" }) blocks[name] = [];
        foreach (var name in new[] { "tie_instances", "shrub_instances", "moby_instances" })
            if (Payload(name).Length == 0) blocks[name] = new byte[0x10];
        var files = UyaConvertedLevelPackageBuilder.Gameplay(blocks).ToList();
        files.AddRange(UyaConvertedLevelPackageBuilder.LevelData(headerBytes, source.PaletteBytes, assets, Find, includeSourceCode: true));
        var nativeHeader = new byte[UyaLevelConstants.LevelWadHeaderSize];
        Write(nativeHeader, 0, nativeHeader.Length); Write(nativeHeader, 8, package.LevelWad.Level);
        Write(nativeHeader, 0x28, package.LevelWad.Occlusion.Offset.Value);
        Write(nativeHeader, 0x2c, package.LevelWad.Occlusion.Size.Value);
        files.Add(new("level_wad/header.bin", nativeHeader, "application/octet-stream"));
        var models = mobys.Where(model => model.ModelOffset > 0).Select(model => new LevelContentModelUse(FrontendAssetKind.Moby, model.ModelId, model.Index))
            .Concat(ties.Where(model => model.ModelOffset > 0).Select(model => new LevelContentModelUse(FrontendAssetKind.Tie, model.ModelId, model.Index)))
            .Concat(shrubs.Where(model => model.ModelOffset > 0).Select(model => new LevelContentModelUse(FrontendAssetKind.Shrub, model.ModelId, model.Index))).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new(files, raw, new Dictionary<int, Vector3>(), 0, models, terrainTextures, present);

        byte[] Find(string path) => package.Files.SingleOrDefault(file => file.Path == path)?.Bytes ?? [];
    }

    private static byte[] ConvertMobys(byte[] bytes)
    {
        var source = Rc1MobyInstancesReader.Read(bytes);
        var output = new byte[checked(0x10 + source.StaticCount * UyaMobyInstancesReader.RecordSize)];
        bytes.AsSpan(0, 0x10).CopyTo(output);
        for (var index = 0; index < source.StaticCount; index++)
        {
            var input = bytes.AsSpan(0x10 + index * Rc1MobyInstancesReader.RecordSize, Rc1MobyInstancesReader.RecordSize);
            var target = output.AsSpan(0x10 + index * UyaMobyInstancesReader.RecordSize, UyaMobyInstancesReader.RecordSize);
            input[0x18..].CopyTo(target[0x28..]);
            Write(target, 0, UyaMobyInstancesReader.RecordSize);
            Write(target, 4, -1);
            // RC1 stores the UID one word earlier than UYA.
            Write(target, 0x10, source.Instances[index].UnknownC);
            Write(target, 0x14, Math.Max(0, source.Instances[index].Unknown10));
            var drawDistance = source.Instances[index].DrawDistance;
            if (!float.IsFinite(drawDistance)) throw new InvalidDataException("RC1 Moby draw distance is not finite.");
            Write(target, 0x30, (int)Math.Clamp((double)drawDistance, 0, int.MaxValue));
        }
        return output;
    }

    private static byte[] ConvertSettings(byte[] bytes)
    {
        _ = Rc1LevelSettingsReader.Read(bytes);
        var output = new byte[UyaLevelSettingsReader.MinimumSize];
        bytes.AsSpan(0, 0x2c).CopyTo(output);
        bytes.AsSpan(0x2c, 0x20).CopyTo(output.AsSpan(0x3c));
        return output;
    }

    private static void Write(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], value);
}
