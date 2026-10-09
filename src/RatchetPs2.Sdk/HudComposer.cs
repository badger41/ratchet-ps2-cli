using RatchetPs2.Core.Games;
using RatchetPs2.Core.Hud;
using RatchetPs2.Games.UYA.Hud;

namespace RatchetPs2.Sdk;

public static class HudComposer
{
    public static HudBankComposition Compose(
        GameId gameId,
        ReadOnlySpan<byte> headerBytes,
        IReadOnlyList<byte[]> bankBytes,
        IReadOnlyList<HudTextureReplacement> replacements,
        IReadOnlyList<HudIconAddition> additions,
        HudBankCompositionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(additions);
        switch (gameId)
        {
            case GameId.UYA:
                for (var index = 0; index < additions.Count; index++)
                {
                    if (!UyaHudSpriteId.IsValidCustomValue(additions[index].SpriteId))
                    {
                        throw new InvalidDataException(
                            $"HUD addition {index} sprite ID {UyaHudSpriteId.Format(additions[index].SpriteId)} " +
                            "is outside the UYA Exxx custom range.");
                    }
                }
                break;
            default:
                throw new NotSupportedException($"HUD composition is not supported for {gameId}.");
        }

        return HudBankComposer.Compose(
            headerBytes, bankBytes, replacements, additions, options, cancellationToken);
    }
}
