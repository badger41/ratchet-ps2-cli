# Box coverage preview

`OcclusionCoverageBuilder.PreviewBoxes(GameId, primary, boxes, cancellationToken)`
evaluates coverage without writing a grid, constructing visibility masks, or
publishing a level. The neutral SDK entry point currently dispatches only UYA.
Pass an empty Primary list when its contribution is disabled; pass only enabled
box sources. The [version-two shape preview](OCCLUSION_SHAPE_COVERAGE.md) supports mixed boxes
and cylinders through the same composition engine. This version-one entry point
retains its original fingerprint and accepts only boxes. Graph filtering is separate.

```csharp
var preview = OcclusionCoverageBuilder.PreviewBoxes(GameId.UYA,
    Array.Empty<OcclusionCoverageCoordinate>(),
    new[] { new OcclusionCoverageBox(new Vector3(40, 40, 20), Quaternion.Identity,
        new Vector3(24, 32, 12)) }, cancellationToken);
```

Inputs use native world coordinates and rotations, not renderer axes. Box dimensions
are positive local dimensions centered at the pose. A box may set `Exclude = true`.
Rotation must be a finite unit quaternion within squared-norm tolerance `0.0001`;
the evaluator normalizes accepted poses in double precision. Dimensions must be
finite, positive and no greater than 1,000,000 on any axis. Centers must be finite.

## Algorithm version 1

`GetBoxPreviewProfile(GameId.UYA)` exposes the current preview limits:

- four-world-unit cells, addressed by integer X/Y/Z;
- structural coordinate range 0–131,069 on each axis;
- at most 1,024 boxes and 100,000 input Primary entries;
- at most 100,000 distinct cells in the inclusion union, before exclusions;
- at most 2,000,000 estimated candidate tests; and
- a fixed boundary tolerance of `0.00001` world units.

These limits describe SDK geometry support, not qualified engine behavior. The
coordinate maximum comes from a 16-bit start plus a 16-bit row count. Combined
spans, tree pointers, mask capacity, and allocation may fail native serialization
even when every coordinate is individually representable. Runtime lookup behavior,
new-cell reserved bits, and safe empty publication still require qualification.

All Primary coordinates and every box's tolerance-expanded world AABB are checked
before enumeration. AABB candidate indices are
`ceil((min - tolerance) / cellSize) - 1` through
`floor((max + tolerance) / cellSize)`. Any candidate outside the profile fails;
nothing is clipped or wrapped. In particular, a box touching world zero also touches
a negative cell and is rejected, including when it is an exclusion. Authoring may
store a broader pose; this preview validates its stricter evaluation boundary.

Inclusion tests each candidate cube against the oriented box on the 15 separating
axes (world axes, box axes, and their cross products). Projection gaps no greater
than the tolerance retain the cell. Near-degenerate cross axes are omitted
conservatively. Calculations use doubles and cached projections, without allocating
per candidate. Face, edge and corner tangency all retain adjacent cells.

Exclusion removes a cube only if its projection is strictly inside every box-local
slab with more than the tolerance to spare. This is equivalent to requiring all
eight corners clearly inside. A touching or uncertain cube remains covered. All
exclusions run against the full inclusion union, including Primary, and removal
happens last. Duplicate cells, source order, or quaternion sign do not change output.

## Results and bounds

`Cells` is a copied read-only array in canonical Z/Y/X order. `SourceCellCounts`
follows the supplied box order: inclusion counts are spatial matches before
deduplication; exclusion counts are fully contained cells in the inclusion union
before any exclusion removes cells. Duplicate sources therefore retain their own
counts. `CandidateTests` reports the actual number of input Primary entries,
inclusion AABB candidates, and exclusion-versus-union tests processed.

Work preflight uses `P + sum(include AABB counts)`, then adds that sum capped at
100,000 times the exclusion count. It validates all inputs and rejects oversized
work before enumerating candidates. The inclusion-union memory limit applies before
exclusion, so a large include cannot bypass it through a large subtracting box.
Cancellation is checked during input validation, at most every 256 candidate tests,
and during fingerprinting. Failure/cancellation returns no partial preview and never
mutates caller-owned collections. This synchronous SDK function belongs in a host
background job; interactive progress and stale-result publication belong to that job.

`CoordinateFingerprint` is lowercase SHA-256 over UTF-8 `uya-box-coverage-v1` plus a
zero byte, then canonical signed little-endian int32 X/Y/Z triples. It identifies
the effective coordinates and algorithm version only. It is not a recipe, source,
scene, visibility, or publication identity. Empty preview is valid and has a stable
fingerprint; it does not authorize serializing or publishing an empty grid.

Run the focused suite (also included by `--uya-occlusion` and the default suite):

```sh
dotnet run --project tests/RatchetPs2.LevelTests --configuration Release -- --uya-occlusion-box-coverage
```

Fixtures cover independent edge/face and corner-containment references over 80 seeded
rotated boxes, thin shapes, tangencies, tolerance, disconnected Primary cells,
exclusion precedence, duplicates/order/quaternion equivalence, stable fingerprints,
structural bounds, malformed inputs, work/cell/source limits, cancellation and a
measured 100,000-cell overlap workload.
