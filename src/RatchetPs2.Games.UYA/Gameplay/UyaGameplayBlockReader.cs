using RatchetPs2.Core.Gameplay;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaGameplayBlockReader
{
    public const int CoreHeaderSize = UyaGameplayLayout.CoreHeaderSize;

    public static UyaGameplayBlocks ReadCore(ReadOnlySpan<byte> data)
    {
        return ReadCore(data, UyaGameplayLayout.Core);
    }

    public static UyaGameplayBlocks ReadCore(ReadOnlySpan<byte> data, GameplayLayout layout)
    {
        var raw = GameplayLayoutReader.Read(data, layout);
        var blocks = raw.Blocks.Select(block =>
        {
            UyaLevelSettings? levelSettings = null;
            if (block.SemanticName == "level_settings"
                && UyaLevelSettingsReader.TryRead(block.PayloadBytes, out var parsedLevelSettings))
            {
                levelSettings = parsedLevelSettings;
            }

            UyaMobyInstances? mobyInstances = null;
            if (block.SemanticName == "moby_instances"
                && UyaMobyInstancesReader.TryRead(block.PayloadBytes, out var parsedMobyInstances))
            {
                mobyInstances = parsedMobyInstances;
            }

            UyaTieInstances? tieInstances = null;
            if (block.SemanticName == "tie_instances")
                UyaTieInstancesReader.TryRead(block.PayloadBytes, out tieInstances);
            UyaShrubInstances? shrubInstances = null;
            if (block.SemanticName == "shrub_instances")
                UyaShrubInstancesReader.TryRead(block.PayloadBytes, out shrubInstances);
            UyaCameraInstances? cameraInstances = null;
            if (block.SemanticName == "cameras")
                UyaCameraInstancesReader.TryRead(block.PayloadBytes, out cameraInstances);
            UyaSoundInstances? soundInstances = null;
            if (block.SemanticName == "sound_instances")
                UyaSoundInstancesReader.TryRead(block.PayloadBytes, out soundInstances);
            UyaCameraCollisionGrid? cameraCollisionGrid = null;
            if (block.SemanticName == "camera_collision_grid" && block.PayloadBytes.Length >= 0x4010)
                cameraCollisionGrid = UyaCameraCollisionGridReader.Read(block.PayloadBytes);

            return new UyaGameplayBlock(
                block.Index,
                block.HeaderOffset,
                block.Pointer,
                block.SemanticName,
                block.PayloadBytes,
                levelSettings,
                mobyInstances,
                tieInstances,
                shrubInstances,
                cameraInstances,
                soundInstances,
                cameraCollisionGrid);
        }).ToArray();

        var tieCount = blocks.FirstOrDefault(block => block.TieInstances is not null)?.TieInstances?.Count ?? 0;
        return new UyaGameplayBlocks(
            layout.Kind,
            layout.HeaderSize,
            raw.HeaderBytes,
            GameplayPvarTableReader.Read(raw.Blocks, layout.GameName),
            blocks,
            GameplayGeometryReader.Read(raw.Blocks),
            UyaGameplayLightingReader.Read(raw.Blocks, tieCount));
    }
}
