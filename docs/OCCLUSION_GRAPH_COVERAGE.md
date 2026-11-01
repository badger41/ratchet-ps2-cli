# Graph coverage preview (development)

`OcclusionCoverageBuilder.PreviewGraphs(GameId, primary, shapes, graphs, triangles,
textures, token, progress)` evaluates explicit scene geometry without files, masks
or native publication. UYA is the only target. This unreleased API complements
`PreviewShapes`/`PreviewBoxes`; those contracts and version-two/version-one identities
remain unchanged. `GetGraphPreviewProfile` exposes graph limits and algorithm 3.

Each `OcclusionCoverageGraph` has a native-space center/unit quaternion, positive
boundary dimensions and owned region-local seed positions. Seeds must be finite,
inside the region and nonempty. Bounds default to a box and optionally use a circular
cylinder. Regions reuse the established double-precision shape/cell intersections
and structural coordinate validation of shape previews. Negative/unsupported
coordinates fail; no clipping occurs. A graph's seeds never filter another graph.

## Visibility contract

Each intersecting four-unit cell tests its center and eight corners inset 0.001
world units along each cell axis. Probes are world-cell points, including portions
of boundary cells outside the authored region. A cell survives if any probe has a
clear finite segment to any seed. This carries forward the Forge v1 interaction as
a coverage heuristic; it does not prove that rejected cells cannot host cameras.
Use ordinary include shapes for scripted, airborne or missed camera routes.

The caller supplies calibrated scene triangles/alpha textures, such as those from
`OcclusionSceneBuilder.BuildCoverageGeometry`. Only triangle flag bit 0 blocks. Target spheres and
barrier planes are not graph blockers; no new classification of dynamic assets is
inferred. Both triangle sides block independently of flag bit 1, matching the
conservative two-sided scene-qualification policy. Invalid groups/flags/geometry,
UVs, texture indices/wraps or alpha dimensions fail before queries.

A UYA-owned BVH accelerates finite segments. Blocked/unblocked queries stop at the
first definite opaque hit; only the buried-seed diagnostic searches for the nearest
hit. Both modes share the same traversal and intersection/alpha rules. Intersection
uses double precision.
Definite interior barycentrics must exceed `1e-8` from every triangle edge. Nearly
parallel intersections (normalized determinant at most `1e-12`), edges, vertices,
coplanarity and hits within 0.001 world units of either segment endpoint do not
block. Raw PS2 bilinear alpha and existing clamp/repeat addressing are reused;
alpha must exceed `127 + 0.00001` to block. Alpha exactly at the threshold is
uncertain and retains coverage. This intentionally retains some leaks along mesh
seams rather than rejecting uncertain coverage. It does not modify visibility-bake
or sample-qualification semantics.

A seed is reported as *apparently buried* only when six fixed oblique directions
all find a definite nearest opaque backface exit. The first such seed fails with
a typed `OcclusionCoverageSeedException` with zero-based `GraphIndex`/`SeedIndex`
and a message with one-based indices and an action to move/remove it. Consumers can
map indices to their authored names and stable IDs. Coincident hits with
opposite windings count as front-facing for this diagnostic, independent of order.
This is an oriented-geometry heuristic, not a complete point-in-solid proof. Open,
transparent and ambiguous meshes remain usable; real-map qualification must examine
both missed buried seeds and possible false positives.

## Sticky surface proximity

`OcclusionCoverageGraph.StickyDistance` is nullable: null preserves existing graph
behavior; a finite value from 0 to 1,000,000 enables Sticky in native map units.
Forge uses an explicit enable flag and a default distance of 12. The caller supplies
`stickyTriangles` to `PreviewGraphs`; omission with Sticky enabled fails rather
than treating arbitrary scene geometry as surface candidates.

`BuildCoverageGeometry(..., includeStickyGeometry: true)` optionally returns
`StickyTriangles`, selecting only TIE/tfrag source faces from the calibrated current
render geometry and sharing its alpha atlas. It excludes shrubs/mobys and never
uses collision data. Scene transforms, winding, blocker flags and material/alpha
classification are the existing extraction results.

Before testing a cell against seeds, Sticky checks the v1 fourteen directions:
eight cube diagonals and six axes. For unnormalized direction `d`, a finite ray
runs from `center - 2*d` to `center + normalize(d)*distance`. Any definite
front-facing opaque hit retains the cell for the normal seed test. Edges and
segment endpoints count as surface contacts; backfaces do not. This retains the
v1 two-unit starting allowance for surfaces crossing an octant, including distance
zero. Raw PS2 alpha still must exceed the existing threshold. This surface-contact
policy does not alter conservative seed-visibility intersections or buried-seed
rules. It is a proximity heuristic, not a walkability or camera-reachability proof.

The existing BVH/query implementation is reused with the surface-contact policy.
Both indexes share one query/ray budget. Visibility-ray preflight remains unchanged;
Sticky rays additionally count against the runtime ray limit, with cancellation at
the same checkpoints. Sticky rejection enters the graph's rejected list and cannot
remove Primary/ordinary include cells; final exclusions still apply last. Disabled
Sticky leaves existing coordinates, identities and query counts unchanged.

`UyaOcclusionStickyCoverageTests` checks air-layer reduction, v1 diagonal probes and
starting offsets, inclusive finite endpoints, winding, alpha, nonblocking surfaces,
composition, invalid distances/missing input, shared budgets and cancellation.

## Current-scene geometry extraction

`OcclusionSceneBuilder.BuildCoverageGeometry(GameId, levelWad, token)` extracts
static terrain/TIE/shrub triangles and alpha textures from a caller-composed level.
It does not require native grid or source-group metadata. Returned triangle groups
are -1; source accounting, visibility masks and target spheres/barriers are absent.
It retains the calibrated material, alpha and blocker policy of the bake extractor.
The strict `Build` bake path keeps its native group/grid validation unchanged.

Input WADs remain bounded to 256 MiB. Coverage extraction rejects nonempty streamed
terrain chunks and more than 100,000 TIE or shrub instances before enumeration;
existing scene geometry/texture budgets still apply. Empty terrain sections support
custom scenes without native terrain. Callers must compose current custom/unsaved
geometry rather than substituting base geometry, retain exact packed-input identity
and reject stale work. Extraction alone does not authorize native publication.

## Composition and identity

The final set unions Primary, ordinary include shapes and accepted graph cells,
then applies ordinary full-containment exclusions last. Primary/shape coverage is
not removed merely because a graph rejects that cell. Shape source counts retain
shape input order. `Graphs` follows graph input order and exposes canonical Z/Y/X
accepted and rejected lists before final exclusion/rescue composition. Lists are
read-only copied results. `Coverage` holds the canonical effective cells, shape
counts, bounded candidate-test count and SHA-256 coordinate identity using
`uya-graph-coverage-v3\0`. Coordinates are little-endian signed 32-bit X/Y/Z triples.
This coordinate identity is not a scene fingerprint or a publication/cache key;
a consumer must capture scene/recipe identity and reject stale work separately.

## Shape-owned graph boundaries

A graph defaults to its existing centered box. Optional `CylinderRadius` uses the
existing exact circular-cylinder/cell intersection implementation, with local Z as
height and Size X/Y equal to the diameter. Size Z remains height. Graph seed local
positions must also satisfy the circular radius, not only the enclosing box.
Rotated and tilted cylinders use the same shape evaluator as plain cylinders;
fully outside corner cells never enter Sticky or line-of-sight queries. Intersecting
boundary cells retain the normal conservative coverage rule. A caller attaches
this filter to its owning volume and supplies that volume's dimensions/pose.
Box behavior, coordinate identities and budgets remain unchanged. The SDK props
advertise `RatchetSdkOcclusionCoverageGraphShapeApiVersion=1` for compatible hosts.

## Bounds, progress and cancellation

Existing geometry/source/Primary/union limits apply, including the 100,000-cell
inclusion union before exclusions and 2,000,000 combined candidate-work ceiling.
Shapes plus graphs may total at most 1,024 sources. Graph limits are 4,096 seeds per
graph, 16,384 combined seeds, 100,000 combined graph bounding-box candidates,
2,000,000 actual rays (including Sticky and buried-seed queries), and
200,000,000 actual BVH-node/triangle query tests. Counts include overlapping inputs.
The calibration follows a copied real 51,948-candidate/three-seed scene:
154,744 triangles required 1,037,980 rays and 97,519,715 query tests (about 1.7 s of
evaluation on the development machine). This is a work bound, not a duration
guarantee. Other limits and cancellation checkpoints remain unchanged.
These are development CPU/memory budgets, not game-engine/native-grid limits.
Candidate bounds are checked before scene indexing/enumeration. Rays are counted
as actually cast against the unchanged shared 2,000,000-ray limit, including
Sticky and seed diagnosis. The evaluator does not reject a theoretical
candidate × nine probes × seed count estimate: Sticky and the first reachable
seed can eliminate most of those rays. Each cell tries its nearest seed first,
then the remaining seeds if needed. This changes query order only: coverage still
accepts any clear connection among all nine probes and all seeds. The nearest-seed
scan is cancellable and runs only after the Sticky filter passes. Geometry is
bounded by existing scene limits (1,000,000 triangles, 4,096 textures, 64 MiB alpha).
The BVH copies triangle indices and never reorders or changes caller geometry.

The optional progress callback reports zero before indexing, each 256 candidate
cells and completion after composition. Totals use the preflight bounding-box
candidate count (one for a zero-graph call). Cancellation is checked while validating,
indexing/sorting, traversing and enumerating, before composition and before return.
Budget failure, callback failure or cancellation returns no partial result. Query
work is measured during traversal; geometries with poor spatial separation can
still hit that cap and fail with a request to reduce regions/seeds/blockers.

## Shared geometry and query indexes

Coverage previews and visibility builds use the same scene ordering and blocker
selection before applying their own output policies. Prepared shapes own candidate
counting, which both ordinary shapes and graph preflight reuse. Graph seed
reachability remains a private part of the UYA evaluator, separate from enumeration.

If every coverage face belongs to a TIE or tfrag, `BuildCoverageGeometry` returns
that existing triangle list as `StickyTriangles`. When the two inputs are the same
list instance, graph evaluation reuses one query index for Sticky and visibility.
A distinct Sticky list keeps its own index and shares the same ray/test budget.
No intersection rules, alpha handling or fingerprint versions change. Buried-seed direction vectors are reused across seeds.

A 2,048-face synthetic Sticky workload retained its exact fingerprint, 1,352 cells,
50,025 rays and 97,277 query tests while measured per-evaluation allocations fell
from 712,816 to 579,344 bytes (18.7%). This measures one fixture, not all map shapes
or geometry mixes. Shared/separate-index regressions verify identical coverage and
work counts; existing cancellation, alpha and buried-seed cases remain in the suite.

## Coherent probes and index construction

Any-hit visibility and Sticky queries retain separate last-hit triangle hints.
Before traversing the BVH, the next probe re-intersects that face with the full
finite-segment, winding, boundary and alpha policy. A miss continues through the
index. Hints never affect nearest-hit buried-seed diagnosis or its opposing-face
tie rules. Each attempted hint counts toward the shared work budget; rays and
cancellation checks remain unchanged.

BVH sorting computes each partition's triangle centroids once and compares cached
values, retaining the original index tie breaker. The standard-library pooled
scratch array is returned after construction, including cancellation/failure.
Caller geometry is never changed and no cross-evaluation cache is retained.

On a copied Hoven scene with 154,744 triangles and 16 seeds, three measured graph
evaluations fell from a median 2.82 seconds to 2.09 seconds. Query tests fell from
171,760,728 to 131,770,409 (23.3%); all runs retained 1,956,909 rays, 13,731 effective
cells and identical coordinate/accepted/rejected hashes. Measured allocations rose
from about 31.5 MB to 32.3 MB per evaluation. These are development-machine fixture
measurements, not guarantees for other scenes.

## Verification

`UyaOcclusionGraphCoverageTests` covers separated rooms, doorways, seeds on both sides,
independent axis-plane segment references, winding/order, alpha threshold/transparent
faces, finite endpoints/seams, buried/coplanar ambiguity, transformed ownership,
rescue shapes/Primary/exclusion precedence, graph deduplication, a golden identity,
invalid inputs, preflight/runtime budgets and cancellation/progress checkpoints.
Retained workloads add 10,000 distant triangles to verify pruning and repeat an
opaque wall to 40,000 overlapping triangles. The latter exceeded the 40-million
query budget with unnecessary nearest-hit searches; it now completes with identical
accepted/rejected cells and the same coordinate identity. A direct blocked-segment
check verifies that a definite hit skips the remaining overlapping blockers. The
nearest-hit buried-seed ambiguity, alpha, finite endpoint, cancellation and budget
checks remain in the same suite. Repeated-hit checks verify a single exact triangle
test, reject a cached face beyond the new endpoint and resample changed alpha. These synthetic checks do not qualify real-map
camera coverage.

```sh
dotnet build tests/RatchetPs2.LevelTests --configuration Release
dotnet tests/RatchetPs2.LevelTests/bin/Release/net10.0/RatchetPs2.LevelTests.dll --uya-occlusion-graph-coverage
dotnet tests/RatchetPs2.LevelTests/bin/Release/net10.0/RatchetPs2.LevelTests.dll --uya-occlusion
```
