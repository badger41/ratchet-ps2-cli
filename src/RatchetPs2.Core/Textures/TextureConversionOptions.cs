namespace RatchetPs2.Core.Textures;

public sealed class TextureConversionOptions
{
    public bool IsSwizzled { get; init; }

    public bool DecodePaletteIndexes { get; init; } = true;

    public bool DoubleAlpha { get; init; }
}