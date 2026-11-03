namespace RatchetPs2.Core.LevelAssets;

public static class LevelAssetTextureExtractor
{
    public static IReadOnlyList<ExtractedLevelAssetTexture> Extract(
        string family,
        byte[] textureIds,
        IReadOnlyList<LevelAssetTextureDefinition> definitions,
        byte[] paletteBytes,
        byte[] assetBytes,
        int textureDataOffset,
        IReadOnlyList<LevelAssetMipmapDefinition>? gsStash,
        bool isSwizzled)
    {
        ArgumentNullException.ThrowIfNull(textureIds);
        ArgumentNullException.ThrowIfNull(definitions);
        var textures = new List<ExtractedLevelAssetTexture>();
        foreach (var textureId in textureIds)
        {
            if (textureId == byte.MaxValue) continue;
            try
            {
                if (textureId >= definitions.Count) throw new InvalidDataException($"{family} texture ID {textureId} is out of range.");
                var texture = LevelAssetReader.BuildAssetTexture(
                    family, textures.Count, definitions[textureId], paletteBytes, assetBytes, textureDataOffset,
                    gsStash, isSwizzled: isSwizzled, useTextureFlags: true);
                textures.Add(new(0, texture.PifBytes));
            }
            catch (Exception exception) when (exception is
                ArgumentException or InvalidDataException or IOException or NotSupportedException or OverflowException)
            {
                textures.Add(new(0, null, exception.Message));
            }
        }
        return textures;
    }
}
