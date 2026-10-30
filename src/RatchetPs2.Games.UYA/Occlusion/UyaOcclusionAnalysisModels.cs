using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
namespace RatchetPs2.Games.UYA.Occlusion;

public sealed record UyaOcclusionBitValue(int Value, int Records);

public sealed record UyaOcclusionFamilyAnalysis(
    string Family,
    int Records,
    int DistinctSecondWords,
    int RepeatedSecondWordsWithDifferentBits,
    int DistinctBits,
    int? BitMin,
    int? BitMax,
    IReadOnlyList<UyaOcclusionBitValue> OutOfRangeBitValues,
    int NeverSetBits,
    int AlwaysSetBits,
    int VisibleRecordsMin,
    int VisibleRecordsP50,
    int VisibleRecordsP95,
    int VisibleRecordsMax,
    double VisibleRecordsMean,
    int RecordsPerBitMax);

public sealed record UyaOcclusionAnalysis(
    int GridBytes,
    int TreeBytes,
    int Octants,
    int ReferencedMaskSlots,
    int DistinctMaskContents,
    int MaxMaskIndex,
    string GridSha256,
    string MappingSha256,
    string SemanticGridSha256,
    int MappingPadding,
    int MappingTrailingBytes,
    bool NoEditMappingWriterByteIdentity,
    int[]? BoundsMin,
    int[]? BoundsMax,
    int MappedBits,
    IReadOnlyList<int> SetUnmappedBits,
    int SharedAcrossFamilies,
    int RecordsPerBitMax,
    int UniqueCornerSamples,
    int UniqueCornerAndCenterSamples,
    long NeighborPairs,
    long EqualNeighborPairs,
    double NeighborHammingMean,
    IReadOnlyList<UyaOcclusionFamilyAnalysis> Families);

// Keys identify native source locations, not Forge Entity IDs or renderer objects.
public sealed record UyaOcclusionSourceLink(
    string SourceKey, string Family, int MappingIndex, int BitIndex, int SecondWord);

public sealed record UyaOcclusionSourceLinks(
    IReadOnlyList<UyaOcclusionSourceLink> Links,
    IReadOnlyList<string> UnresolvedMappings,
    IReadOnlyList<string> UnmappedSources,
    IReadOnlyList<string> AmbiguousSources,
    int PrimaryTfragGroups,
    int ChunkTfragGroups,
    int TfragTriangleCountMatches,
    int TfragTriangleCountMismatches,
    int TfragStashGroups,
    int MobyFlagZeroInstances);

public sealed record UyaOcclusionLevelAnalysis(
    string Status,
    UyaOcclusionAnalysis? Grid,
    UyaOcclusionSourceLinks? Sources,
    int ExternalMappingBytes,
    string ExternalMappingSha256,
    bool? ExternalMappingPrefixEqualsGameplay,
    int OcclusionOctreeOffset,
    int OcclusionRadiusOffset,
    int OcclusionRadius2Offset,
    IReadOnlyList<int> ChunkTerrainSources);
