using RatchetPs2.Core.Gameplay;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Games.UYA.Collision;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelContentInventoryReader
{
    public static IReadOnlyList<LevelContentFamily> ReadConverted(LevelContentConversion converted, GameId sourceGame, CancellationToken cancellationToken = default)
    {
        var files = converted.Files.ToDictionary(file => file.Path, file => file.Bytes, StringComparer.Ordinal);
        var result = new List<LevelContentFamily>();
        Add("ties", "tie_instances", bytes => UyaTieInstancesReader.Read(bytes).Instances.Select((item, index) => Item("ties", index, item.ClassId)).ToArray());
        Add("shrubs", "shrub_instances", bytes => UyaShrubInstancesReader.Read(bytes).Instances.Select((item, index) => Item("shrubs", index, item.ClassId)).ToArray());
        Add("mobys", "moby_instances", bytes => UyaMobyInstancesReader.Read(bytes).Instances.Select((item, index) => Item("mobys", index, item.ClassId)).ToArray());
        Add("cuboids", "cuboids", bytes => Indexed("cuboids", GameplayGeometryReader.ReadCuboids(bytes).Length));
        Add("spheres", "spheres", bytes => Indexed("spheres", GameplayGeometryReader.ReadShapes(bytes, "sphere").Length));
        Add("splines", "splines", bytes => Indexed("splines", GameplayGeometryReader.ReadSplines(bytes).Length));
        Add("paths", "grind_splines", bytes => Indexed("paths", GameplayGeometryReader.ReadGrindPaths(bytes).Length));
        Add("areas", "areas", bytes => Indexed("areas", GameplayGeometryReader.ReadAreas(bytes).Length));
        Add("directionalLights", "directional_lights", bytes => Indexed("directionalLights", UyaGameplayLightingReader.ReadDirectionalLights(bytes).Length));
        Add("levelSettings", "level_settings", _ => [new("source:levelSettings:singleton")]);
        Capture("mapGroups", files.Any(file => file.Key.EndsWith("_groups.bin", StringComparison.Ordinal) && file.Value.Length > 0), [], _ =>
        {
            var items = new List<LevelContentItem>();
            foreach (var (section, kind) in new[] { ("moby_groups", "moby"), ("tie_groups", "tie"), ("shrub_groups", "shrub") })
            {
                var bytes = files.GetValueOrDefault($"gameplay/core/{section}.bin") ?? [];
                if (bytes.Length == 0) continue;
                items.Add(new($"source:mapGroups:{kind}:table", ItemCount: 0));
                items.AddRange(UyaTieGroupsReader.Read(bytes).Groups.Select((_, index) => new LevelContentItem($"source:mapGroups:{kind}:{index}")));
            }
            return items.ToArray();
        }, allowEmpty: true, estimatedBytes: files.Where(file => file.Key.EndsWith("_groups.bin", StringComparison.Ordinal)).Sum(file => (long)file.Value.Length));
        var headerBytes = files["assets/asset_header.bin"];
        var storedAssets = files["assets/asset_wad.bin"];
        var assets = BinaryMagic.IsWad(storedAssets)
            ? WadCompression.Decompress(storedAssets, new(), cancellationToken) : storedAssets;
        var h = LevelAssetReader.ReadHeader(headerBytes);
        var offsets = LevelAssetReader.CollectKnownAssetOffsets(h, assets.Length,
            LevelAssetReader.ReadModelDefinitions(headerBytes, h.MobyModelOffset, h.MobyModelCount),
            LevelAssetReader.ReadModelDefinitions(headerBytes, h.TieModelOffset, h.TieModelCount),
            LevelAssetReader.ReadShrubDefinitions(headerBytes, h.ShrubModelOffset, h.ShrubModelCount), [h.SceneViewSize]);
        Asset("tfrags", h.TerrainOffset, bytes => bytes.AsSpan().ContainsAnyExcept((byte)0) ? [new("source:tfrags:primary:0", ItemCount: TfragTerrainReader.Read(bytes).TfragCount)] : [], true);
        Asset("collision", h.CollisionOffset, bytes =>
        {
            var mesh = UyaCollisionReader.Read(bytes);
            return mesh.SolidPieces.Select(piece => new LevelContentItem($"source:collision:primary:0:solid:{piece.SourceIndex}"))
                .Concat(mesh.PlayerBarriers.Select(piece => new LevelContentItem($"source:collision:primary:0:barrier:{piece.SourceIndex}"))).ToArray();
        });
        Asset("skybox", h.SkyOffset, bytes =>
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return RatchetPs2.Core.Skyboxes.SkyboxReader.Read(stream, GameId.UYA).Shells
                .Select(shell => new LevelContentItem($"source:skybox:{shell.Index}")).ToArray();
        });
        for (var index = 1; index < converted.ChunkSlotCount; index++)
        {
            if (!files.TryGetValue($"level_wad/chunks/chunk{index}.wad", out var chunk)) continue;
            var terrain = TfragChunkWadReader.ReadTerrainPayload(chunk);
            var collision = TfragChunkWadReader.ReadCollisionPayload(chunk);
            Append("tfrags", terrain, bytes => bytes.AsSpan().ContainsAnyExcept((byte)0)
                ? [new($"source:tfrags:chunk:{index}", ItemCount: TfragTerrainReader.Read(bytes).TfragCount)] : []);
            Append("collision", collision, bytes => {
                var mesh = UyaCollisionReader.Read(bytes);
                return mesh.SolidPieces.Select(piece => new LevelContentItem($"source:collision:chunk:{index}:solid:{piece.SourceIndex}"))
                    .Concat(mesh.PlayerBarriers.Select(piece => new LevelContentItem($"source:collision:chunk:{index}:barrier:{piece.SourceIndex}"))).ToArray();
            });
        }
        var hudBytes = files.Where(file => file.Key.StartsWith("hud/", StringComparison.Ordinal)).Sum(file => (long)file.Value.Length);
        result.Add(new("hud", hudBytes > 0, hudBytes > 0 ? [new("source:hud:singleton")] : [], hudBytes));
        result.Add(new("fx", h.FxTextureCount > 0, h.FxTextureCount > 0 ? [new("source:fx:singleton")] : [], 0));
        Add("occlusion", "occlusion", _ => [new("source:occlusion:singleton")]);
        result.Add(new("opaqueContent", false, [], 0));
        return result;

        void Append(string family, byte[] bytes, Func<byte[], LevelContentItem[]> read)
        {
            if (bytes.Length == 0) return;
            var previous = result.Single(value => value.Family == family);
            result.Remove(previous);
            Capture(family, true, bytes, read);
            var current = result[^1];
            result[^1] = current with { Items = previous.Items.Concat(current.Items).ToArray(),
                EstimatedBytes = previous.EstimatedBytes + current.EstimatedBytes, Diagnostic = previous.Diagnostic ?? current.Diagnostic };
        }
        void Add(string family, string name, Func<byte[], LevelContentItem[]> read)
        {
            var bytes = files.GetValueOrDefault($"gameplay/core/{name}.bin") ?? [];
            Capture(family, converted.PresentGameplaySections?.Contains(name) ?? bytes.Length > 0, bytes, read);
        }
        void Asset(string family, int offset, Func<byte[], LevelContentItem[]> read, bool zero = false) =>
            Capture(family, zero || offset > 0, LevelAssetReader.ReadAssetSlice(assets, offset, offsets, zero), read);
        void Capture(string family, bool present, byte[] bytes, Func<byte[], LevelContentItem[]> read, bool allowEmpty = false, long? estimatedBytes = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var items = !present || bytes.Length == 0 && !allowEmpty ? [] : read(bytes);
                if (items.Sum(item => (long)item.ItemCount) > 100_000) throw new InvalidDataException("Content inventory exceeds the item limit.");
                result.Add(new(family, present, items, estimatedBytes ?? bytes.Length));
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or EndOfStreamException)
            {
                var message = $"{sourceGame} {family}: {error.Message}";
                result.Add(new(family, present, [], estimatedBytes ?? bytes.Length, message.Length > 512 ? message[..512] : message));
            }
        }
    }

    private static LevelContentItem Item(string family, int index, int classId) => new($"source:{family}:{index}", classId);
    private static LevelContentItem[] Indexed(string family, int count) => Enumerable.Range(0, count).Select(index => new LevelContentItem($"source:{family}:{index}")).ToArray();
}
