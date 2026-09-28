using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures.Palettes;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaMobyTeamPaletteComposer
{
    public static byte[] Rewrite(
        byte[] source,
        IReadOnlyList<(TexturePaletteAssignment Assignment, OptimizedPalette Palette)> materials)
    {
        if (source.Length <= 0x0b || source[0x0b] == 0) return source;
        using var stream = new MemoryStream(source, writable: false);
        var model = MobyModelReader.Read(stream, new() { SkipAnimationSequences = true });
        var paletteCount = model.TeamPalettes & 0x0f;
        var textureCount = model.TeamPalettes >> 4;
        if (paletteCount == 0 || textureCount == 0 || model.TeamPaletteDataOffset <= 0
            || model.TeamPaletteData.Count != textureCount || materials.Count < textureCount)
            throw new InvalidDataException("UYA moby embedded team palette metadata is invalid.");

        var output = source.ToArray();
        for (var textureIndex = 0; textureIndex < textureCount; textureIndex++)
        {
            if (!model.TeamPaletteData.TryGetValue(textureIndex, out var palettes)
                || palettes.Count != paletteCount)
                throw new InvalidDataException("UYA moby embedded team palette count is invalid.");
            for (var paletteIndex = 0; paletteIndex < paletteCount; paletteIndex++)
            {
                var offset = checked(model.TeamPaletteDataOffset
                    + (textureIndex * paletteCount + paletteIndex) * 0x400);
                if (offset < 0 || offset + 0x400 > output.Length
                    || !source.AsSpan(offset, 0x400).SequenceEqual(palettes[paletteIndex]))
                    throw new InvalidDataException("UYA moby embedded team palette range is invalid.");
                PaletteTextureWriter.RewritePalette(
                        palettes[paletteIndex], materials[textureIndex].Assignment, materials[textureIndex].Palette)
                    .CopyTo(output, offset);
            }
        }
        return output;
    }
}
