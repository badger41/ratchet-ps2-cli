using RatchetPs2.Core.Games;
using RatchetPs2.Core.Sounds;
using RatchetPs2.Games.UYA;

namespace RatchetPs2.Sdk;

public static class SoundBankReader
{
    public static SoundBankInventory Inspect(
        GameId gameId,
        byte[] bankBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bankBytes);
        return gameId switch
        {
            GameId.UYA or GameId.DL => Snd989SoundBankReader.Inspect(bankBytes, cancellationToken),
            _ => throw new NotSupportedException($"Sound-bank inventory is not supported for {gameId}."),
        };
    }

    public static SoundPcmPreview RenderPreview(
        GameId gameId,
        byte[] bankBytes,
        int programIndex,
        int maximumFrames = 2_000_000,
        bool preferAudible = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bankBytes);
        return gameId switch
        {
            GameId.UYA or GameId.DL => Snd989SoundBankReader.RenderPreview(
                bankBytes, programIndex, maximumFrames, preferAudible, cancellationToken),
            _ => throw new NotSupportedException($"Sound preview is not supported for {gameId}."),
        };
    }

    public static int PreviewSampleRate(GameId gameId, sbyte centerNote, sbyte centerFine) =>
        gameId switch
        {
            GameId.UYA or GameId.DL => Snd989SoundBankReader.PreviewSampleRate(centerNote, centerFine),
            _ => throw new NotSupportedException($"Sound preview is not supported for {gameId}."),
        };

    public static SoundRemapInventory InspectRemap(
        GameId gameId,
        byte[] assetHeaderBytes,
        int programCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetHeaderBytes);
        return gameId switch
        {
            GameId.UYA => UyaSoundRemapReader.Inspect(
                assetHeaderBytes, programCount, cancellationToken),
            _ => throw new NotSupportedException($"Sound-remap inventory is not supported for {gameId}."),
        };
    }
}
