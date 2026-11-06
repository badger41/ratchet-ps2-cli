using System.Numerics;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Core.LevelAssets;

public sealed record LevelContentConversion(
    IReadOnlyList<PackedFile> Files,
    byte[] RawGameplay,
    IReadOnlyDictionary<int, Vector3> SkyAngularVelocitiesRadiansPerSecond,
    int ChunkSlotCount,
    IReadOnlyList<LevelContentModelUse> Models,
    IReadOnlyList<byte[]>? TerrainTextures = null,
    IReadOnlySet<string>? PresentGameplaySections = null,
    bool HasSceneSettings = true);

public sealed record LevelContentModelUse(FrontendAssetKind Kind, int ClassId, int SourceIndex);
