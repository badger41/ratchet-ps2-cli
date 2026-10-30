# UYA occlusion discovery and qualification

This is read-only analysis for Forge M18-001. It does not generate visibility,
interpret negative mapping sentinels, or write game data.

Production ownership:

- `RatchetPs2.Core.Occlusion` owns neutral geometry data, sample planning and corner qualification.
- `RatchetPs2.Games.UYA.Occlusion` owns native decoding/composition and UYA visibility rules.
- Desktop callers use `RatchetPs2.Sdk.OcclusionSceneBuilder` and `OcclusionComposer`, dispatching with `GameId`.
- Forge owns its GPU transfer protocol, WebGPU execution, project cache and editor presentation.

Production extraction preserves the calibrated spatial triangle order for raster
batch culling, but does not construct diagnostic BVH nodes or sampled source reports.
CPU calibration remains a bounded reference check, not a production raycasting bake.

Build and run from the SDK repository root:

```bash
dotnet build tests/RatchetPs2.LevelTests/RatchetPs2.LevelTests.csproj \
  --configuration Release -p:BuildInParallel=false -m:1
dotnet run --no-build --configuration Release \
  --project tests/RatchetPs2.LevelTests/RatchetPs2.LevelTests.csproj -- \
  --uya-occlusion
dotnet run --no-build --configuration Release \
  --project tests/RatchetPs2.LevelTests/RatchetPs2.LevelTests.csproj -- \
  --qualify-uya-occlusion <clean-uya-ntsc-u.iso> <report.json>
```

The corpus command runs the synthetic fixtures, verifies supported disc metadata
and full-image MD5, and analyzes one level at a time. Ctrl+C cancels between safe
checkpoints. A report is serialized, validated, flushed, and atomically published
only after every level succeeds. Failure or cancellation preserves the previous
report. Source image access is read-only. Reports contain counts, hashes, and at
most 16 source-key examples per diagnostic category, without native payloads.

`UyaOcclusionAnalyzer.Analyze` accepts the asset grid and gameplay mapping bytes.
It reports family statistics, shared bits, explicit out-of-range bit values, mask
sharing, neighbor disagreement, reusable corner/center counts, source hashes,
and no-edit mapping-writer identity. Visibility statistics exclude negative or
out-of-range bit values; their runtime meaning is not inferred. Percentiles and
means count every cell reference, including cells sharing a mask.

The semantic grid hash orders cells Z/Y/X and hashes little-endian int32 X/Y/Z plus
128 mask bytes per cell. It is independent of mask-pool slot assignment. The byte
hash still distinguishes native layout changes.

`UyaOcclusionLevelAnalyzer.Analyze` locates native sections through existing SDK
readers and calls `UyaOcclusionSourceLinker.Link`. Native source keys identify
primary/chunk tfrag group indices and tie/moby instance indices. They are not Forge
Entity IDs. The linker uses each tfrag's occlusion index, tie occlusion IDs, and
moby UIDs. It retains all matching rows and exposes missing/ambiguous associations.
Nonzero tfrag stash selectors remain unresolved. The second tfrag mapping word is
reported as native metadata with triangle-count correlation, rather than assumed
to be a unique object ID. This matters on streamed maps and exceptional inventories.

Primary terrain represents the chunk-zero domain; additional chunk-one/two terrain
is linked separately. Source keys retain these domains. A future solver must
respect active streaming state when selecting blockers; combining every chunk into
one solid occluder scene has not been qualified.

Analysis caps are 100,000 cells, 100,000 mapping/source records, 200,000 source
associations, 32 MiB of grid bytes, and 256 MiB of loose level-WAD input. Existing
WAD decompression limits also apply. The raw grid/mapping reader overloads accept
explicit cardinality limits and cancellation; their original signatures remain.
Tree walking also caps visited table entries, including sparse or aliased tables
that emit no cells. Model table counts and spans are checked before allocation.
Level unpacking and some existing section readers are synchronous, so cancellation
is observed around those bounded operations rather than during every decode step.

The version-2 report records full source identity, SDK informational version and
assembly hash, runtime, deterministic structural statistics, and source-link hashes.
It does not establish a frozen runtime lookup contract, GPU performance budget,
or correctness of regenerated/packed occlusion.

## Production regression checks

`--uya-occlusion` runs the native analysis, shared sample planner and qualification,
alpha/UV reference, calibrated depth ownership, nonblocking moby bounds, ocean,
mesh LOD, native mask writer, and Hoven wall regression fixtures. These checks are
self-contained and do not bake a level or require a GPU.

The experimental probes, policy sweeps, BVHs, replay tools and export/compare
commands have been retired. The production GPU worker still checks bounded CPU
calibration rays, and Forge retains sample visualization and frozen-octant views.

## Native allocation and mask compaction

`OcclusionComposer.ReplaceMasks` remains an exact native mask writer. Production
bakes use `RebuildGrid`, which unions similar masks only as needed to fit the source
grid's byte allocation. The merge score counts added group bits across every octant
affected by the merge. Initial candidates connect face-adjacent cells; deterministic
bounded candidates connect disconnected islands when necessary. Exact duplicate
unions share a slot immediately. Reserved bits never cross merge domains.

The compactor validates every generated bit as a subset of the final mask, verifies
native readback and coordinates, and rejects output exceeding the original byte
allocation. It observes cancellation and bounds cell count, candidate work and the
pending queue. An impossible budget fails before publication.

`CompactGrid` accepts existing generated grids. `ApplyToLevel` applies the same
budget before packing, so compatible raster caches work without another GPU bake.
Forge also applies it when loading cached viewport masks. This changes only mask
sharing and conservative visibility; sample positions and raster settings are
unchanged. Pixel sampling remains GPU accelerated; compaction uses CPU bitsets.

`--uya-occlusion-compaction` runs the focused budget, conservative union, weighted
cost, exact interning, disconnected cell, reserved bit, cancellation, determinism
and cache compatibility regressions. It is also part of `--uya-occlusion`.

## Reflected instance winding

Scene extraction reverses B/C and their matching UVs when a TIE or shrub instance
has a negative basis determinant. This preserves authored front/back orientation
after reflection for corner qualification and the nearby-backface exit rule.
Outpost octant #10375 has a mirrored ramp case covered by a local fixture at all
six pull-in settings. Transform tests also preserve texture-alpha interpolation.

Forge profile v21 includes this correction. Earlier v20/v19 masks remain available
for viewport inspection, but a fresh v21 bake is required before packing them as
current visibility. Mask compaction continues to enforce the source allocation.
