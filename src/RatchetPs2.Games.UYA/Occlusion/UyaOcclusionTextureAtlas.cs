using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Games.UYA.Occlusion;

internal sealed class UyaOcclusionTextureAtlas
{
    private readonly byte[] headerBytes, palette, assets;
    private readonly LevelAssetHeader header;
    private readonly Dictionary<string, IReadOnlyList<LevelAssetTextureDefinition>> tables = [];
    private readonly Dictionary<(string Family, int Index), int> cache = [];
    private readonly IReadOnlyList<LevelAssetMipmapDefinition> stash;
    private long alphaBytes;
    public List<OcclusionAlphaTexture> Textures { get; } = [];

    public UyaOcclusionTextureAtlas(byte[] headerBytes, byte[] palette, byte[] assets)
    {
        this.headerBytes = headerBytes; this.palette = palette; this.assets = assets;
        header = LevelAssetReader.ReadHeader(headerBytes);
        var mipCount = checked(header.GsRamCount + header.ExtraMipmapCount);
        CheckTable(header.GsRamOffset, mipCount, 16);
        if (header.GsRamCount < 0 || header.ExtraMipmapCount < 0) throw new InvalidDataException("Negative occlusion mip count.");
        stash = LevelAssetReader.ReadMipmapDefinitions(headerBytes, header.GsRamOffset, mipCount).Skip(header.GsRamCount).ToArray();
        AddTable("tfrag", header.TerrainTextureOffset, header.TerrainTextureCount);
        AddTable("tie", header.TieTextureOffset, header.TieTextureCount);
        AddTable("shrub", header.ShrubTextureOffset, header.ShrubTextureCount);
    }

    public int Resolve(string family, int material, byte[]? modelTextureIds = null)
    {
        if (material < 0) return -1;
        var index = material;
        if (modelTextureIds is not null)
        {
            index = -1; var slot = 0;
            foreach (var id in modelTextureIds)
            {
                if (id == 255) continue;
                if (slot++ == material) { index = id; break; }
            }
            if (index < 0) throw new InvalidDataException($"{family} material {material} has no texture binding.");
        }
        if (cache.TryGetValue((family, index), out var found)) return found;
        var definitions = tables[family];
        if ((uint)index >= definitions.Count) throw new InvalidDataException($"{family} texture {index} is outside its table.");
        var definition = definitions[index];
        var count = checked((long)definition.Width * definition.Height);
        if (definition.Width is < 1 or > 4096 || definition.Height is < 1 or > 4096
            || alphaBytes + count > UyaOcclusionLimits.MaximumAlphaBytes || Textures.Count >= UyaOcclusionLimits.MaximumTextures)
            throw new InvalidDataException("Occlusion texture exceeds the bounded atlas limit.");
        var asset = LevelAssetReader.BuildAssetTexture(family, index, definition, palette, assets, header.TextureDataOffset,
            family == "tfrag" ? stash : null, isSwizzled: false, useTextureFlags: true);
        var image = TextureConverter.Decode(PifReader.Read(asset.PifBytes), new TextureConversionOptions { DoubleAlpha = false });
        var alpha = new byte[checked((int)count)];
        for (var i = 0; i < alpha.Length; i++) alpha[i] = image.PixelData[i * 4 + 3];
        found = Textures.Count; Textures.Add(new(image.Width, image.Height, alpha) { Family = family, SourceTextureIndex = index }); alphaBytes += count;
        cache.Add((family, index), found); return found;
    }

    private void AddTable(string family, int offset, int count)
    {
        CheckTable(offset, count, 16);
        tables.Add(family, LevelAssetReader.ReadTextureDefinitions(headerBytes, offset, count));
    }
    private void CheckTable(int offset, int count, int stride)
    {
        if (count < 0 || count > 100_000 || (count > 0 && (offset < 0 || (long)offset + (long)count * stride > headerBytes.Length)))
            throw new InvalidDataException("Occlusion texture table exceeds its source or count limit.");
    }
}
