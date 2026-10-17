using RatchetPs2.Core.Games;

namespace RatchetPs2.Core.Gltf;

public static class StaticModelNativeLimits
{
    public const int MaximumMaterials = 16;
    public const int MaximumPackets = 255;
    public const int MaximumModelBytes = 64 * 1024 * 1024;
}

public sealed record StaticModelNativeTexture(
    string AssetId,
    int Width,
    int Height,
    int PaletteIndex,
    byte[] PifBytes);

public sealed record StaticModelNativeMaterialBinding(
    int NativeMaterialIndex,
    int? SourceMaterialIndex,
    string TextureAssetId,
    bool ClampU,
    bool ClampV);

public sealed record StaticModelNativePrimitiveBinding(
    int SourceMeshIndex,
    int SourcePrimitiveIndex,
    int NativeMaterialIndex,
    int FirstTriangle,
    int TriangleCount,
    int FirstPacket,
    int PacketCount);

public sealed record StaticModelNativeDiagnostic(
    string Code,
    string Message,
    int? SourceMeshIndex = null,
    int? SourcePrimitiveIndex = null,
    int? SourceMaterialIndex = null,
    int? SourceTextureIndex = null);

public sealed record StaticModelNativeConversionResult(
    GameId GameId,
    int TargetClassId,
    byte[] DefinitionBytes,
    byte[] ModelBytes,
    string ModelSha256,
    IReadOnlyList<StaticModelNativeTexture> Textures,
    IReadOnlyList<StaticModelNativeMaterialBinding> Materials,
    IReadOnlyList<StaticModelNativePrimitiveBinding> Primitives,
    IReadOnlyList<byte[]> MaterialTexturePifBytes,
    IReadOnlyList<StaticModelNativeDiagnostic> Diagnostics,
    string LodPolicy);
