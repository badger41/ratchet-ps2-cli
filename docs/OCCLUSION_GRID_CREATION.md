# Source-free occlusion grid creation

`OcclusionComposer.CreateGrid(GameId, masks, cancellationToken)` serializes a
nonempty coordinate-to-mask dictionary without an original grid. The UYA
implementation shares its encoder with `ReplaceMasks`; source-preserving rebuild,
validation, compaction, and source-only level application keep their existing
coordinate restrictions.

```csharp
var visible = new byte[128];
visible[0] = 1;
var grid = OcclusionComposer.CreateGrid(GameId.UYA,
    new Dictionary<(int X, int Y, int Z), byte[]>
    {
        [(10, 20, 30)] = visible,
        [(12, 20, 30)] = visible,
    }, cancellationToken);
```

The separate [shape coverage preview](OCCLUSION_SHAPE_COVERAGE.md) can evaluate shape
coordinates without constructing visibility masks or publishing a level.

Coordinates are native cell coordinates, not world positions or renderer axes.
The caller supplies every complete 128-byte mask, including bit 1023. Creation
does not infer a reserved-bit default, qualify camera coverage, bake visibility,
or publish an edited level. Native engine behavior for new cells, empty grids,
out-of-coverage cameras, and allocation growth must be qualified by the consuming
workflow before enabling publication.

The encoder:

- accepts 1–100,000 distinct cells and rejects null or incorrectly sized masks;
- orders cells by Z/Y/X and interns identical complete masks deterministically;
- validates nonnegative encoded coordinates and 16-bit row starts/counts using
  wide span arithmetic, rejecting unrepresentable ranges rather than wrapping;
- measures the actual unaliased lookup-tree layout and checks each four-byte-unit
  16-bit row pointer before allocating the output buffer;
- accepts at most 65,535 unique masks, with `0xffff` reserved for missing cells;
- aligns the mask table to 16 bytes and the final payload to 64 bytes;
- preserves all supplied visibility/reserved bits, never mutates caller data, and
  checks cancellation while validating, planning, and writing; and
- re-reads the output to verify the exact coordinate set and every complete mask.

These are encoding limits, not a qualified gameplay coordinate or memory profile.
In particular, row starts and row ends differ: an unsigned 16-bit start plus an
entry offset can describe a coordinate greater than 65,535. Sparse spans can exceed
pointer limits below the cell-count limit. Empty input is rejected until the
consuming workflow establishes a safe empty-publication contract.

Run focused creation and existing occlusion checks:

```sh
dotnet run --project tests/RatchetPs2.LevelTests --configuration Release -- --uya-occlusion-create-grid
dotnet run --project tests/RatchetPs2.LevelTests --configuration Release -- --uya-occlusion
```

Fixtures cover an independently encoded golden cell, new/moved/removed cells,
sparse gaps, explicit reserved bits, mask interning, input-order independence,
alignment/readback, invalid coordinates/spans/masks, cell/mask/pointer budgets,
cancellation, unsupported games, and caller-owned data.

## Applying authored coverage to a level

`OcclusionComposer.ApplyCoverageToLevel(GameId, levelWad, masks, cancellationToken)`
creates a grid from the same explicit, complete masks and composes it through the
existing asset composer and level archive builder. It permits replacement coordinates
and allocation growth/shrinkage. `ApplyToLevel` retains its source-coordinate
validation and source-allocation compaction policy.

When the source has no grid, the asset composer inserts it at the scene-view boundary,
before assets outside that view. It updates the grid pointer, scene-view/asset lengths,
known adjacent asset pointers and trailing sequence pointers. A missing or invalid
scene-view boundary is rejected. Existing opaque bytes are preserved. Packed output
is re-read and must match the entire generated grid before a result is returned.
No file is published by this in-memory API; consumers own atomic publication and
engine-memory qualification. It does not create visibility masks or infer reserved bits.

Complete-level fixtures retain deterministic changed coordinates, allocation growth
across sectors, shrinkage, missing-grid insertion both before trailing assets and at
asset end, sequence relocation, unrelated palette/sound/auxiliary payloads, invalid
boundaries, source-only rejection, explicit reserved bits and input ownership.

The overload accepting `(levelWad, generatedGrid, octantCount, cancellationToken)`
validates and reads a cached generated grid, then routes its complete masks through
the same creator/application path. It rejects mismatched counts, empty/malformed
coverage and cancellation; it does not relax `ApplyToLevel`. The development SDK
advertises this integration as `RatchetSdkOcclusionCoverageGenerationApiVersion=1`
in `OcclusionCoveragePreview.props`, independently of preview support.
