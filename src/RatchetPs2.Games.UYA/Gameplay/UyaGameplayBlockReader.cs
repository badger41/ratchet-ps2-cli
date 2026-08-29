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

            return new UyaGameplayBlock(
                block.Index,
                block.HeaderOffset,
                block.Pointer,
                block.SemanticName,
                block.PayloadBytes,
                levelSettings,
                mobyInstances);
        }).ToArray();

        return new UyaGameplayBlocks(
            layout.Kind,
            layout.HeaderSize,
            raw.HeaderBytes,
            blocks,
            GameplayGeometryReader.Read(raw.Blocks));
    }
}
