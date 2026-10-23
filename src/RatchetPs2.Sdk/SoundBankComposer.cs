using RatchetPs2.Core.Games;
using RatchetPs2.Core.Sounds;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public static class SoundBankComposer
{
    public static SoundBankCompositionResult Compose(
        GameId gameId,
        ReadOnlySpan<byte> sourceBankBytes,
        IReadOnlyList<SoundBankProgramInput> programs,
        IReadOnlyList<SoundBankSampleInput> samples,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA or GameId.DL => Snd989SoundBankComposer.Compose(
                sourceBankBytes, programs, samples, cancellationToken),
            _ => throw new NotSupportedException($"Sound-bank composition is not supported for {gameId}."),
        };

    public static byte[] ComposeRemap(
        GameId gameId,
        ReadOnlySpan<byte> assetHeaderBytes,
        SoundRemapComposition composition,
        CancellationToken cancellationToken = default) => gameId switch
        {
            GameId.UYA => UyaSoundRemapComposer.ComposeHeader(
                assetHeaderBytes, composition, cancellationToken),
            _ => throw new NotSupportedException($"Sound-remap composition is not supported for {gameId}."),
        };
}
