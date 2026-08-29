using RatchetPs2.Core.Gameplay;

namespace RatchetPs2.Games.DL.Gameplay;

public static class DlGameplayBlockReader
{
    public const int CoreHeaderSize = DlGameplayLayout.CoreHeaderSize;
    public const int MissionHeaderSize = DlGameplayLayout.MissionHeaderSize;

    public static DlGameplayBlocks ReadCore(ReadOnlySpan<byte> data)
    {
        return Read(data, DlGameplayLayout.Core);
    }

    public static DlGameplayBlocks ReadMission(ReadOnlySpan<byte> data)
    {
        return Read(data, DlGameplayLayout.Mission);
    }

    private static DlGameplayBlocks Read(ReadOnlySpan<byte> data, GameplayLayout layout)
    {
        var raw = GameplayLayoutReader.Read(data, layout);
        var blocks = raw.Blocks.Select(block =>
        {
            DlLevelSettings? levelSettings = null;
            if (layout == DlGameplayLayout.Core
                && block.SemanticName == "level_settings"
                && DlLevelSettingsReader.TryRead(block.PayloadBytes, out var parsedLevelSettings))
            {
                levelSettings = parsedLevelSettings;
            }

            DlMobyInstances? mobyInstances = null;
            if (block.SemanticName == "moby_instances"
                && DlMobyInstancesReader.TryRead(block.PayloadBytes, out var parsedMobyInstances))
            {
                mobyInstances = parsedMobyInstances;
            }

            return new DlGameplayBlock(
                block.Index,
                block.HeaderOffset,
                block.Pointer,
                block.SemanticName,
                block.PayloadBytes,
                levelSettings,
                mobyInstances);
        }).ToArray();

        return new DlGameplayBlocks(
            layout.Kind,
            layout.HeaderSize,
            raw.HeaderBytes,
            blocks,
            GameplayGeometryReader.Read(raw.Blocks));
    }
}
