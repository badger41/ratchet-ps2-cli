using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Textures.Palettes;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Games.RC1.Level;

public static class Rc1TextureConversionRepair
{
    public static bool MatchesBase(IReadOnlyList<FrontendAssetTexture> original,
        IReadOnlyList<FrontendAssetTexture> updated, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(updated);
        token.ThrowIfCancellationRequested();
        if (original.Count != updated.Count) return false;
        for (var index = 0; index < original.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (original[index].Role != updated[index].Role) return false;
            var before = PifReader.Read(original[index].PifBytes);
            var after = PifReader.Read(updated[index].PifBytes);
            if (before.Encoding != after.Encoding || before.IsSwizzled != after.IsSwizzled
                || before.Header.USize != after.Header.USize || before.Header.VSize != after.Header.VSize
                || !before.PaletteData.AsSpan().SequenceEqual(after.PaletteData)
                || !before.PixelData.AsSpan().SequenceEqual(after.PixelData)) return false;
        }
        return true;
    }

    public static IReadOnlyList<FrontendAssetTexture>? RepairToUya(IReadOnlyList<FrontendAssetTexture> original,
        IReadOnlyList<FrontendAssetTexture> updated, IReadOnlyList<FrontendAssetTexture> native,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(updated);
        ArgumentNullException.ThrowIfNull(native);
        if (native.Count != original.Count || !MatchesBase(original, updated, token)) return null;
        var textures = native.ToArray();
        var changed = false;
        for (var index = 0; index < textures.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var previous = original[index];
            if (previous.Role != textures[index].Role) continue;
            var expected = UyaLevelAssetModelConverter.ConvertLinearTexture(previous.PifBytes, (TextureRole)previous.Role);
            if (!expected.AsSpan().SequenceEqual(textures[index].PifBytes)) continue;
            var corrected = UyaLevelAssetModelConverter.ConvertLinearTexture(updated[index].PifBytes, (TextureRole)previous.Role);
            if (corrected.AsSpan().SequenceEqual(expected)) continue;
            textures[index] = textures[index] with { PifBytes = corrected };
            changed = true;
        }
        return changed ? textures : null;
    }
}
