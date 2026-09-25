using RatchetPs2.Core.Gameplay;

namespace RatchetPs2.Games.UYA.Gameplay;

public sealed record UyaGameplayBlocks(
    string Kind,
    int HeaderSize,
    byte[] HeaderBytes,
    GameplayPvarTables? PvarTables,
    IReadOnlyList<UyaGameplayBlock> Blocks,
    GameplayGeometry Geometry,
    UyaGameplayLighting Lighting);

public sealed record UyaGameplayBlock(
    int Index,
    int HeaderOffset,
    int Pointer,
    string SemanticName,
    byte[] PayloadBytes,
    UyaLevelSettings? LevelSettings = null,
    UyaMobyInstances? MobyInstances = null,
    UyaTieInstances? TieInstances = null,
    UyaShrubInstances? ShrubInstances = null,
    UyaCameraInstances? CameraInstances = null,
    UyaSoundInstances? SoundInstances = null,
    UyaCameraCollisionGrid? CameraCollisionGrid = null);
