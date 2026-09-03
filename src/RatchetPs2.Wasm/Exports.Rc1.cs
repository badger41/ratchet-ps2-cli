using Microsoft.JSInterop;
using RatchetPs2.Core.Gameplay;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Gameplay;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.RC1.Gameplay;
using RatchetPs2.Games.RC1.Level;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace RatchetPs2.Wasm;

[SupportedOSPlatform("browser")]
public static partial class Exports
{
    [JSInvokable("ParseRc1GameplayCore")]
    public static WasmDlGameplayBlocks ParseRc1GameplayCore(byte[] gameplayBytes)
    {
        ArgumentNullException.ThrowIfNull(gameplayBytes);

        var gameplay = Rc1Gameplay.ReadCore(ReadGameplayPayload(gameplayBytes));
        var geometry = GameplayGeometryReader.Read(gameplay.Blocks);
        return new WasmDlGameplayBlocks(
            gameplay.Layout.Kind,
            gameplay.Layout.HeaderSize,
            ToWasmPvarTables(gameplay.Blocks, "RC1"),
            new WasmGameplayGeometry(geometry.Cuboids, geometry.Splines, geometry.Areas),
            gameplay.Blocks.Select(block => new WasmDlGameplayBlock(
                block.Index,
                block.HeaderOffset,
                block.Pointer,
                block.SemanticName,
                block.PayloadBytes.Length,
                block.SemanticName == "level_settings"
                    && Rc1LevelSettingsReader.TryRead(block.PayloadBytes, out var levelSettings)
                        ? ToWasmLevelSettings(levelSettings)
                        : null,
                block.SemanticName == "moby_instances"
                    && Rc1MobyInstancesReader.TryRead(block.PayloadBytes, out var mobys)
                        ? ToWasmMobyInstances(mobys)
                        : null)).ToArray());
    }

    [JSInvokable("BuildRc1LevelWadRenderPackage")]
    public static PackedFilePackage BuildRc1LevelWadRenderPackage(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        var package = Rc1LevelWadUnpacker.Unpack(levelWadBytes);
        var renderFiles = Rc1LevelWadRenderPackageBuilder.BuildFiles(
            package.LevelWad.Level,
            package.Files,
            assets => DlLevelWadRenderPackageBuilder.BuildAssetFiles(
                GameId.RC1,
                package.LevelWad.Level,
                assets.HeaderBytes,
                assets.PaletteBytes,
                assets.AssetWadBytes,
                DlLevelWadRenderPackageBuildOptions.Browser with
                {
                    AssetProfile = Rc1LevelAssetProfile.Default
                }));
        return PackRenderFiles(renderFiles, package.Files);
    }

    [JSExport]
    public static byte[] BuildRc1LevelWadRenderPackageEnvelope(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return BuildRenderPackageEnvelope(BuildRc1LevelWadRenderPackage(levelWadBytes));
    }

    private static WasmDlLevelSettings? ToWasmLevelSettings(Rc1LevelSettings? settings)
    {
        return settings is null
            ? null
            : new WasmDlLevelSettings(
                new DlRgb96(settings.BackgroundColor.Red, settings.BackgroundColor.Green, settings.BackgroundColor.Blue),
                new DlRgb96(settings.FogColor.Red, settings.FogColor.Green, settings.FogColor.Blue),
                settings.FogNearDistance,
                settings.FogFarDistance,
                settings.FogNearIntensity,
                settings.FogFarIntensity,
                settings.DeathHeight,
                false,
                new DlVector3(0, 0, 0),
                new DlVector3(settings.ShipPosition.X, settings.ShipPosition.Y, settings.ShipPosition.Z),
                settings.ShipRotationZ,
                settings.ShipPath,
                settings.ShipCameraCuboidStart,
                settings.ShipCameraCuboidEnd,
                0,
                [],
                0,
                null,
                [],
                null,
                null,
                0,
                0);
    }

    private static WasmDlMobyInstances? ToWasmMobyInstances(Rc1MobyInstances? mobyInstances)
    {
        return mobyInstances is null
            ? null
            : new WasmDlMobyInstances(
                mobyInstances.StaticCount,
                mobyInstances.SpawnableMobyCount,
                mobyInstances.Pad8,
                mobyInstances.PadC,
                mobyInstances.Instances.Select((instance, index) => new DlMobyInstance(
                    instance.Size,
                    0,
                    index,
                    0,
                    instance.ClassId,
                    instance.Scale,
                    float.IsFinite(instance.DrawDistance) ? (int)instance.DrawDistance : 0,
                    instance.UpdateDistance,
                    instance.Unused28,
                    instance.Unused2C,
                    new DlVector3(instance.Position.X, instance.Position.Y, instance.Position.Z),
                    new DlVector3(instance.Rotation.X, instance.Rotation.Y, instance.Rotation.Z),
                    instance.Group,
                    instance.IsRooted,
                    float.IsFinite(instance.RootedDistance) ? instance.RootedDistance : 0,
                    instance.Unknown54,
                    instance.PvarIndex,
                    instance.Occlusion,
                    instance.ModeBits,
                    new DlRgb96(instance.Color.Red, instance.Color.Green, instance.Color.Blue),
                    instance.Light,
                    instance.Unknown74)).ToArray(),
                mobyInstances.TrailingBytes.Length);
    }

    private static WasmDlPvarTables? ToWasmPvarTables(
        IReadOnlyList<GameplayRawBlock> blocks,
        string gameName)
    {
        return ToWasmPvarTables(
            FindPayload(blocks, "pvar_moby_links"),
            FindPayload(blocks, "pvar_table"),
            FindPayload(blocks, "pvar_data"),
            FindPayload(blocks, "pvar_relative_pointers"),
            gameName);
    }

    private static byte[] FindPayload(IReadOnlyList<GameplayRawBlock> blocks, string semanticName)
    {
        return blocks.FirstOrDefault(block => block.SemanticName == semanticName)?.PayloadBytes ?? [];
    }
}
