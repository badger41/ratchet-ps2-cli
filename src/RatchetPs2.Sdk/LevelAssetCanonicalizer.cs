using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.RC1.Level;

namespace RatchetPs2.Sdk;

public static class LevelAssetCanonicalizer
{
    public static byte[] NormalizeDefinition(GameId game, ReadOnlySpan<byte> definition) => game switch
    {
        GameId.UYA => UyaLevelAssetCanonicalizer.NormalizeDefinition(definition),
        GameId.DL => DlLevelAssetCanonicalizer.NormalizeDefinition(definition),
        GameId.GC => GcLevelAssetCanonicalizer.NormalizeDefinition(definition),
        GameId.RC1 => Rc1LevelAssetCanonicalizer.NormalizeDefinition(definition),
        _ => throw new NotSupportedException($"Canonical assets are not supported for {game}."),
    };

    public static void Validate(GameId game, ReadOnlySpan<byte> definition, ReadOnlySpan<byte> model,
        IReadOnlyList<FrontendAssetTexture> textures)
    {
        switch (game)
        {
            case GameId.UYA: UyaLevelAssetCanonicalizer.Validate(definition, model, textures); break;
            case GameId.DL: DlLevelAssetCanonicalizer.Validate(definition, model, textures); break;
            case GameId.RC1: Rc1LevelAssetCanonicalizer.Validate(definition, model, textures); break;
            case GameId.GC: GcLevelAssetCanonicalizer.Validate(definition, model, textures); break;
            default: throw new NotSupportedException($"Canonical assets are not supported for {game}.");
        }
    }
}
