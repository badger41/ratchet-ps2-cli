namespace RatchetPs2.Core.Wad.Models;

public sealed record IsoLevelAllocation(int LevelIndex, int HeaderSector, int CapacitySectors);

public sealed record IsoPatchRange(
    string Name,
    long Offset,
    int Length,
    int Alignment,
    string SourceSha256,
    string OutputSha256,
    ReadOnlyMemory<byte> OutputBytes);

public sealed record IsoPatchPlan(
    int SchemaVersion,
    int LevelIndex,
    long IsoLength,
    int HeaderSector,
    int PayloadBaseSector,
    int CapacitySectors,
    int RequiredSectors,
    bool FitsInPlace,
    string SourceLevelWadSha256,
    string OutputLevelWadSha256,
    IReadOnlyList<IsoPatchRange> Ranges,
    string StrategyReason,
    IsoReplacementPlan? Replacement)
{
    public IsoLevelAllocation Allocation => new(LevelIndex, HeaderSector, CapacitySectors);
}

public sealed record IsoReplacementPlan(
    int HeaderSector,
    int PayloadBaseSector,
    long OutputIsoLength,
    long RequiredFreeBytes,
    ReadOnlyMemory<byte> LevelWadBytes);
