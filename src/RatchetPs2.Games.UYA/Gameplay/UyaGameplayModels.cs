using RatchetPs2.Core.Gameplay;

namespace RatchetPs2.Games.UYA.Gameplay;

public sealed record UyaGameplayBlocks(
    string Kind,
    int HeaderSize,
    byte[] HeaderBytes,
    IReadOnlyList<UyaGameplayBlock> Blocks,
    GameplayGeometry Geometry);

public sealed record UyaGameplayBlock(
    int Index,
    int HeaderOffset,
    int Pointer,
    string SemanticName,
    byte[] PayloadBytes,
    UyaLevelSettings? LevelSettings = null,
    UyaMobyInstances? MobyInstances = null);
