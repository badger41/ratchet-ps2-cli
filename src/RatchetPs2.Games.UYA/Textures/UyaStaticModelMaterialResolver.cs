using System.Numerics;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Games.UYA.Textures;

internal static class UyaStaticModelMaterialResolver
{
    private const int Repeat = 10_497;
    private const int MirroredRepeat = 33_648;
    private const int ClampToEdge = 33_071;

    public static PreparedMaterial[] PrepareUsed(
        StaticModelCanonicalAsset asset,
        List<StaticModelNativeDiagnostic> diagnostics) => asset.Primitives
        .Select(value => value.MaterialIndex)
        .Distinct()
        .OrderBy(value => value ?? int.MaxValue)
        .Select((value, index) => Resolve(asset, value, index, diagnostics))
        .ToArray();

    public static PreparedMaterial Resolve(
        StaticModelCanonicalAsset asset,
        int? sourceIndex,
        int nativeIndex,
        List<StaticModelNativeDiagnostic>? diagnostics = null)
    {
        if (sourceIndex is null)
            return new(nativeIndex, null, null, Vector4.One, "OPAQUE", 0.5f, false, false, false);
        if (sourceIndex < 0 || sourceIndex >= asset.Materials.Count)
            throw new InvalidDataException($"Source material {sourceIndex} is outside the canonical material table.");
        var material = asset.Materials[sourceIndex.Value];
        if (material.BaseColorFactor is null || material.BaseColorFactor.Length != 4
            || material.BaseColorFactor.Any(value => !float.IsFinite(value) || value is < 0 or > 1))
            throw new InvalidDataException($"Source material {sourceIndex} has an invalid base-color factor.");
        if (material.AlphaMode is not ("OPAQUE" or "MASK" or "BLEND"))
            throw new InvalidDataException($"Source material {sourceIndex} has unsupported alpha mode {material.AlphaMode}.");
        if (!float.IsFinite(material.AlphaCutoff) || material.AlphaCutoff is < 0 or > 1)
            throw new InvalidDataException($"Source material {sourceIndex} has an invalid alpha cutoff.");
        if (material.MetallicFactor != 1f || material.RoughnessFactor != 1f)
            diagnostics?.Add(new(
                "pbr-approximation",
                "UYA static-model materials do not retain metallic/roughness factors.",
                SourceMaterialIndex: sourceIndex));

        int? imageIndex = null;
        var clampU = false;
        var clampV = false;
        if (material.BaseColorTextureIndex is { } textureIndex)
        {
            if (textureIndex < 0 || textureIndex >= asset.Textures.Count)
                throw new InvalidDataException($"Source material {sourceIndex} references missing texture {textureIndex}.");
            var texture = asset.Textures[textureIndex];
            if (texture.ImageIndex < 0 || texture.ImageIndex >= asset.Images.Count)
                throw new InvalidDataException($"Source texture {textureIndex} references missing image {texture.ImageIndex}.");
            if (texture.SamplerIndex < 0 || texture.SamplerIndex >= asset.Samplers.Count)
                throw new InvalidDataException($"Source texture {textureIndex} references missing sampler {texture.SamplerIndex}.");
            imageIndex = texture.ImageIndex;
            var sampler = asset.Samplers[texture.SamplerIndex];
            ValidateWrap(sampler.WrapS, sourceIndex.Value, textureIndex, "S");
            ValidateWrap(sampler.WrapT, sourceIndex.Value, textureIndex, "T");
            clampU = sampler.WrapS == ClampToEdge;
            clampV = sampler.WrapT == ClampToEdge;
            if (sampler.WrapS == MirroredRepeat || sampler.WrapT == MirroredRepeat)
                diagnostics?.Add(new(
                    "mirrored-repeat-approximation",
                    "UYA static-model output maps mirrored repeat to repeat.",
                    SourceMaterialIndex: sourceIndex,
                    SourceTextureIndex: textureIndex));
        }

        return new(
            nativeIndex,
            sourceIndex,
            imageIndex,
            new(material.BaseColorFactor[0], material.BaseColorFactor[1],
                material.BaseColorFactor[2], material.BaseColorFactor[3]),
            material.AlphaMode,
            material.AlphaCutoff,
            material.DoubleSided,
            clampU,
            clampV);
    }

    private static void ValidateWrap(int wrap, int materialIndex, int textureIndex, string axis)
    {
        if (wrap is not (Repeat or MirroredRepeat or ClampToEdge))
            throw new InvalidDataException(
                $"Source material {materialIndex}, texture {textureIndex} has unsupported {axis}-axis wrap mode {wrap}.");
    }
}
