namespace RatchetPs2.Core.Gltf;

public static class StaticModelCanonicalFormat
{
    public const int Version = 1;
}

public static class StaticModelGltfLimits
{
    public const int MaximumDocumentBytes = 16 * 1024 * 1024;
    public const int MaximumDependencyBytes = 64 * 1024 * 1024;
    public const int MaximumTotalDependencyBytes = 256 * 1024 * 1024;
    public const int MaximumCanonicalBytes = 256 * 1024 * 1024;
    public const int MaximumUriLength = 1024;
    public const int MaximumNodeDepth = 64;
    public const int MaximumBuffers = 32;
    public const int MaximumBufferViews = 65_536;
    public const int MaximumAccessors = 65_536;
    public const int MaximumMeshes = 4_096;
    public const int MaximumNodes = 65_536;
    public const int MaximumPrimitives = 65_536;
    public const int MaximumMaterials = 4_096;
    public const int MaximumTextures = 4_096;
    public const int MaximumImages = 1_024;
    public const int MaximumVertices = 4_000_000;
    public const int MaximumIndices = 12_000_000;
    public const int MaximumImageDimension = 8_192;
    public const int MaximumImagePixels = 16_777_216;
    public const int MaximumTotalImagePixels = 67_108_864;
}

public enum StaticModelDependencyKind
{
    Buffer = 1,
    Image = 2,
}

public sealed record StaticModelDependency(string Uri, StaticModelDependencyKind Kind);

public sealed record StaticModelImportFile(string Uri, byte[] Bytes);

public sealed record StaticModelSourceDependency(
    string Uri,
    StaticModelDependencyKind Kind,
    string Sha256);

public sealed record StaticModelImportProvenance(
    string SourceName,
    string SourceSha256,
    IReadOnlyList<StaticModelSourceDependency> Dependencies);

public sealed record StaticModelBounds(
    float MinimumX,
    float MinimumY,
    float MinimumZ,
    float MaximumX,
    float MaximumY,
    float MaximumZ);

public sealed record StaticModelPrimitive(
    int SourceMeshIndex,
    int SourcePrimitiveIndex,
    string? Name,
    int? MaterialIndex,
    float[] Positions,
    float[]? Normals,
    float[]? TexCoords,
    float[]? Colors,
    uint[] Indices);

public sealed record StaticModelSampler(
    int MagFilter,
    int MinFilter,
    int WrapS,
    int WrapT);

public sealed record StaticModelTexture(
    string? Name,
    int ImageIndex,
    int SamplerIndex);

public sealed record StaticModelMaterial(
    string? Name,
    float[] BaseColorFactor,
    int? BaseColorTextureIndex,
    float MetallicFactor,
    float RoughnessFactor,
    string AlphaMode,
    float AlphaCutoff,
    bool DoubleSided);

public sealed record StaticModelImage(
    string? Name,
    int Width,
    int Height,
    byte[] Rgba32);

public sealed record StaticModelCanonicalAsset(
    int FormatVersion,
    IReadOnlyList<StaticModelPrimitive> Primitives,
    IReadOnlyList<StaticModelSampler> Samplers,
    IReadOnlyList<StaticModelTexture> Textures,
    IReadOnlyList<StaticModelMaterial> Materials,
    IReadOnlyList<StaticModelImage> Images,
    StaticModelBounds Bounds);

public sealed record StaticModelImportResult(
    StaticModelCanonicalAsset Asset,
    byte[] CanonicalBytes,
    string ContentSha256,
    StaticModelImportProvenance Provenance,
    IReadOnlyList<StaticModelNativeDiagnostic> Diagnostics);
