# Shape coverage preview

`OcclusionCoverageBuilder.PreviewShapes(GameId, primary, shapes, cancellationToken)`
evaluates a mixed box/cylinder recipe without generating masks, writing a grid or
publishing a level. Only UYA is implemented. Input coordinates and poses are native
world coordinates, with local Z as the cylinder axis. Pass only enabled sources and
an empty Primary list when Primary contribution is disabled.

```csharp
OcclusionCoverageShape[] shapes =
[
    new OcclusionCoverageBox(new Vector3(40, 40, 20), Quaternion.Identity,
        new Vector3(24, 32, 12)),
    new OcclusionCoverageCylinder(new Vector3(40, 40, 20), Quaternion.Identity,
        Radius: 6, Height: 10, Exclude: true),
];
var preview = OcclusionCoverageBuilder.PreviewShapes(GameId.UYA,
    Array.Empty<OcclusionCoverageCoordinate>(), shapes, cancellationToken);
```

Both shapes are centered at their pose, with finite centers and unit quaternions
(squared-norm tolerance `0.0001`, normalized internally in double precision).
Box dimensions, cylinder radius and cylinder height must be positive, finite and
no greater than 1,000,000. Radius is circular; there is no scale or elliptical mode.
Unknown shape subclasses fail before candidate enumeration.

`OcclusionCoverageBuilder.ReadCoordinates(GameId, grid, cancellationToken)` reads
Primary coordinates from an existing native grid through the neutral SDK boundary.
The reader checks native structure, bounded table-entry work, cancellation and a
100,000-cell limit. It does not modify source bytes or infer visibility/reserved-bit
values for new cells. An empty readable grid returns an empty coordinate list;
empty native publication remains a separate qualification gate.

## Shared composition and bounds

`GetPreviewProfile(GameId.UYA)` returns algorithm version 2. It retains the
[box preview's](OCCLUSION_BOX_COVERAGE.md) four-unit cells, structural coordinate
range 0–131,069, maximum 1,024 combined shapes, 100,000 input Primary entries,
100,000 distinct cells in the inclusion union before exclusion, 2,000,000 estimated
candidate tests and `0.00001` world-unit boundary tolerance.

Primary and include sources union first. Exclusions remove only fully contained
cell cubes and always run last, regardless of source kind/order. Per-source counts
follow input order; every exclusion counts against the same inclusion union before
removal. Counts, canonical Z/Y/X read-only coordinates and actual candidate work
retain the box evaluator's ownership, preflight and cancellation contracts.
Preflight bounds all candidate/exclusion tests before enumeration; cancellation is
checked at most every 256 candidates. The synchronous evaluator belongs in a host
background job; interactive progress and stale-result handling belong to that job.

For cylinders, candidate AABBs enclose the tolerance-expanded radius and half-height,
then receive the shared AABB boundary tolerance. A pose touching an unsupported
candidate, including negative cells at world zero, fails without clipping. Sparse
tree capacity, native allocation, runtime coordinate behavior, new-cell reserved bits
and safe empty publication are separate qualification gates. Empty preview remains
valid. This API does not establish a publishable native grid.

## Circular cylinder inclusion

In the cylinder's normalized local frame, inclusion uses radius `r + tolerance` and
half-height `h / 2 + tolerance`. It clips the candidate cube by the two cap planes.
The clipped convex solid's vertices consist of cube corners within the height slab
and original cube-edge intersections with its cap planes (at most 32 retained points,
including duplicates). Projecting those vertices onto local XY and taking their
convex hull gives the entire radial projection of the clipped solid.

The cube intersects the finite cylinder exactly when this polygon intersects its
cross-section disk: the polygon contains the origin or its closest boundary point
is no farther than the expanded radius. Point/segment projections are handled as
degenerate polygons. This uses the circular surface directly; no polygonal cylinder
approximation, ray sampling, or iterative convergence threshold controls inclusion.
A constant-size stack workspace and cached corner offsets avoid per-candidate heap
allocation. Face, edge, cap and rim contacts retain coverage.

Exclusion requires every cube corner strictly inside both the height slab shrunk by
the tolerance and the circular radius shrunk by the tolerance. Convexity then places
the entire cube inside. A nonpositive shrunken radius/half-height cannot exclude
cells. Tangent and uncertain containment retain coverage. Spinning about the local
cylinder axis changes no geometry and cannot produce an elliptical cross-section.

## Identity and compatibility

`CoordinateFingerprint` is lowercase SHA-256 over UTF-8 `uya-shape-coverage-v2` plus
a zero byte, then canonical little-endian signed int32 X/Y/Z triples. It identifies
effective coordinates and this algorithm version, not source/recipe/scene/visibility
inputs or accepted publication. Source order, duplicate contributions and quaternion
sign do not change the coordinate fingerprint.

`PreviewBoxes` and `GetBoxPreviewProfile` remain available with algorithm version 1
and the original `uya-box-coverage-v1` fingerprint prefix. Both APIs share composition
and box geometry; box coordinates, counts, work limits, and the version-one golden
fingerprint remain unchanged. A version-two result deliberately has a different
fingerprint even when coordinates equal a version-one result.

## Verification

```sh
dotnet run --project tests/RatchetPs2.LevelTests --configuration Release -- --uya-occlusion-cylinder-coverage
dotnet run --project tests/RatchetPs2.LevelTests --configuration Release -- --uya-occlusion
```

The suite retains analytical circle/rectangle and height-interval comparisons for
30 seeded axis-aligned cylinders, an independent SAT reference using inscribed and
circumscribed 64-sided prisms for 40 seeded rotated cylinders, independent corner
containment for exclusions, and the existing 80-box edge/face reference. Prism
uncertainty is bounded and recorded in assertions, rather than used as production
geometry. Fixtures also cover circular cap/rim/corner tangency, clear separation,
thin and subnormal dimensions, high coordinates, local-axis spins, mixed-source
precedence, fingerprints, unsupported shapes/games, malformed data, shared budgets,
cancellation and a measured overlapping rotated-cylinder workload.

The development [graph preview API](OCCLUSION_GRAPH_COVERAGE.md) composes accepted
graph cells with these shapes under a separate version-three coordinate identity.
It does not change the shape-only API, geometry rules or version-two fingerprints.

## Host capability declaration

`src/RatchetPs2.Sdk/OcclusionCoveragePreview.props` declares
`RatchetSdkOcclusionCoveragePreviewApiVersion=1` for this SDK checkout. A host can
import it to enable the matching shape/graph preview API at build time. The SDK
project imports the same declaration; older releases without this file do not
advertise preview support. This declares API availability, not native grid/mask
publication qualification or a reserved-bit default.
