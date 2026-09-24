# Full frontend SDK coverage

C# remains authoritative. Generate executable JavaScript and TypeScript declarations
from Core and all four Games projects. New supported C# features must be discovered
without maintaining source lists, wrappers, or copied domain models.

Full coverage means every reusable public API is accounted for: executable browser
bindings with parity checks, or an explicit host-only diagnostic with a usable
byte/stream equivalent. Do not silently omit unsupported APIs. Keep the default
full-library build failing until its unsupported API/compiler inventory is resolved.
Filesystem and CLI orchestration remain host responsibilities; in-memory streams
and all map/asset parsing and conversion remain in scope.

## Tasks in dependency order

- [x] **T01 — Automatic discovery and baseline.** MSBuild source/reference discovery,
  Roslyn dependency closure, generated bindings/declarations, failure cleanup and
  machine-readable full-library report. Existing discovery regression passes.
  Baseline: 386 public types, 274 binding errors, 2,406 compiler diagnostics;
  diagnostic totals include repeated/cascading errors and are not coverage percentages.
- [x] **T02 — Binary buffers and primitives.** Adapt byte array/span conversions,
  slices/ranges, copies, equality, indexing, allocation and binary reads/writes.
  Check shared views versus copies, offsets, overlapping copies, bounds and endian
  values against .NET. Subtasks below keep this milestone small enough to verify.
- [x] **T02a — Byte views.** AsSpan overloads with integer offsets/lengths, Slice,
  literal range slicing, indexed reads/simple writes, ToArray, SequenceEqual,
  Span-to-ReadOnlySpan conversion and UTF-8 literals. Preserve shared storage for
  both frontend Uint8Array inputs and Transpose's internally allocated arrays;
  generate accurate span-view declarations. Verify with `tests/check-buffers.mjs`.
- [x] **T02b — Remaining buffer operations.** CopyTo/TryCopyTo (including overlapping
  source/destination), Clear/Fill, default/empty spans, array-to-span conversions,
  Index/Range values and compound/ref indexing. Lower required stack allocations
  without changing observable ownership or initialization. Extend the same parity
  probe; unsupported forms must fail explicitly until covered.
- [x] **T02c — Complete binary primitives.** Add the remaining discovered endian
  readers/writers and BitConverter overloads. Compare bytes and numeric boundaries
  with .NET, including float bit patterns, negative values and short destinations.
- [x] **T03 — C# arithmetic semantics.** Preserve checked conversions/arithmetic,
  overflow, unsigned operations and 64-bit values. Test boundaries and evaluation
  order against .NET; never remove checked guards just to compile.
- [x] **T04 — Numerics.** Supply the Vector2/3/4, Quaternion and Matrix4x4 operations
  actually used by discovered sources. Verify transforms, handedness, composition,
  float rounding and degenerate inputs against .NET.
- [x] **T05 — Complete value bindings.** Generate model/options inputs, collections,
  dictionaries, nullable values, enums and memory buffers; map numeric structs.
  Support required instance lifetimes, static properties and ref/out signatures.
  Account explicitly for generic/cyclic models. Check strict TS consumers and
  JS round trips, validation, defaults, ownership and mutation semantics.
- [x] **T05a — Numeric, collection and static value bindings.** Map numeric structs
  in both directions, including nullable values and projected collections. Accept
  primitive, buffer and numeric collection inputs without mutating caller arrays,
  and bind readable static properties. Verify validation, float32 normalization,
  ownership, mutation behavior and strict TypeScript declarations.
- [x] **T05b — Domain values and object lifetimes.** Add recursive model/options,
  dictionaries, sets, tuples, enums and memory buffers, followed by the required
  instance/ref/out lifetime contracts. Keep cyclic/reference models explicit.
- [x] **T05b1 — Recursive model and options inputs.** Generate structural input
  declarations and native C# constructors for acyclic domain values, including
  nested models/collections, nullable values, required members and property-default
  preservation. Reject cyclic and non-constructible shapes explicitly.
- [x] **T05b2 — Maps, sets, tuples, enums and memory.** Map dictionaries and sets to
  native JavaScript `Map`/`Set`, value tuples to readonly tuples, 64-bit inputs to
  validated `bigint`, and byte memory to owned `Uint8Array` results. Preserve nested
  model/numeric conversion and compile `IReadOnlySet`/`ReadOnlyMemory` browser uses.
- [x] **T05b3 — Object lifetimes and multi-out results.** Retain native transpiled
  instances behind branded model facades with live getters and generated instance
  methods/constructors. Accept SDK-owned cyclic or non-constructible values in later
  calls, and preserve every output from boolean methods with multiple `out` values.
- [x] **T06 — In-memory I/O and archives.** Support Stream/MemoryStream,
  BinaryReader/Writer and required compression/ZIP operations. Separate filesystem
  orchestration from reusable operations. Verify seeks, EOF, malformed/truncated
  archives and decompressed/serialized bytes against .NET. Decide sync/async browser
  adaptations from actual callers before adding a dependency.
- [x] **T06a — Byte-backed streams and binary I/O.** Map Stream and BinaryReader/Writer
  parameters to retained in-memory stream facades, use Transpose's MemoryStream, and
  supply the missing modern Stream overloads. Verify seeks, EOF, copied inputs,
  serialized bytes, real PIF read/write calls and strict TypeScript declarations.
- [x] **T06b — Synchronous compression and ZIP.** Adapt the synchronous ZLibStream and
  custom-map ZipArchive callers to the frontend's existing `fflate` dependency. Verify
  valid, malformed and truncated zlib/ZIP data and extracted bytes against .NET.
- [x] **T07 — JSON and remaining BCL.** Cover System.Text.Json DOM/serialization and
  remaining discovered collections, text/regex, encoding and other runtime gaps.
  Verify actual glTF/material/metadata contracts, missing versus null values and
  error behavior. Re-run full discovery to catch gaps beyond the initial inventory.
- [x] **T07a — Read-only JSON DOM and SDK values.** Map `JsonDocument`, `JsonElement`,
  `JsonProperty` and `JsonValueKind` to native browser JSON. Accept recursively
  validated `JsonValue` inputs and parse byte/stream inputs with fatal UTF-8 decoding.
  Verify glTF inspection, raw JSON results, missing versus null, malformed input,
  boundary rejection and strict TypeScript declarations with `tests/check-json.mjs`.
- [x] **T07b — JSON serialization and mutable nodes.** Cover the discovered serializer
  options, enum conversion, ignored members and `JsonNode`/`JsonObject`/`JsonArray`
  mutation used by glTF, metadata and render-package exporters.
- [x] **T07c — Remaining BCL inventory.** Cover the remaining path, collection, text,
  regex, encoding, hashing and timing operations, then refresh full discovery until
  only explicitly host-bound APIs remain.
- [x] **T08 — Reusable frontend entry points.** Move reusable orchestration from
  Wasm exports into Core/per-game library services; leave host wrappers thin.
  Expose WAD/custom-map bytes → render assets + gameplay with automatic bindings.
  Existing .NET/WASM callers must retain their behavior.
- [x] **T09 — UYA end to end.** Load a real standard WAD and custom ZIP through
  generated JS. Compare package paths/bytes and gameplay with .NET, then render in
  map-o-matic. This is the first complete frontend replacement checkpoint.
- [ ] **T10 — RC1, GC and DL coverage.** Repeat package/gameplay parity and browser
  rendering for each game, including DL custom maps and mission data. Exercise
  asset import/export and remaining reusable APIs, not only the viewer path.
- [ ] **T11 — Normal package/build integration.** Wire the promoted generator into
  frontend builds,
  CI and release output. Replace WASM initialization/calls and obsolete asset
  copying after the corresponding paths pass. Verify a clean checkout build.
- [ ] **T12 — Release acceptance.** No unresolved browser API or compiler errors;
  every host-only API explicitly accounted for. Run discovery regression, strict
  declarations, .NET/JS parity and browser checks. Measure startup, parse time,
  bundle size and peak memory on representative maps; report results, not assumed
  speedups. Document supported browser/runtime and ownership/error contracts.

## Working rules

Fix shared runtime/compiler capabilities before adding per-feature adaptations.
Use selective builds while migrating, and regenerate the full report after each
compatibility milestone. A smaller diagnostic count alone is not a passing test.
Keep completed substeps and exact verification commands below so work can resume.

## Progress

- Moved the generator's JavaScript/TypeScript acceptance suite from the project
  root into `tests/`; npm scripts remain the stable entry points.
- Fixed compound-assignment RHS grouping in the browser source rewrite. Serializing
  `span[i] += delta << 2` without parentheses shifted the base value as well as the
  delta, inflating DL compact animation transforms. Preserve grouping for integer
  spans and the equivalent numeric-vector rewrite; buffer/numerics parity checks
  now cover these cases. DL level06 moby `0x0e7d` matches native .NET across all six
  clips (12,501 transform components); all five exported glTF scale tracks match exactly.
  The frontend production worker check now uses this level and asserts the affected
  joint's scale keys. Rebuilt all four SDK bundles and bumped the frontend cache
  version so production cannot reuse packages with the incorrect transforms.
- Removed the retired browser host and wrapper generator after all four games
  migrated to the TypeScript SDK. Libraries now target only `net10.0`; solution,
  project references, discovery and release restore no longer select browser TFMs.
  Earlier migration entries below describe the historical transition, not active hosts.
  Verified Release solution build (zero warnings/errors), full SDK generation with
  `--verify`, parser parity, frontend entry points, strict runtime/consumer typing,
  and frontend `check:sdk-worker` (production build and four workers for every game).
- Extracted all 104 JavaScript templates into dedicated `Runtime/*.ts` ES modules,
  along with the static JSON, compression, numeric and stream helpers previously
  embedded in `SdkPackage.cs`. C# templates now contain only call bindings; Roslyn
  rewrites still operate on C# syntax. Every generator run compiles the runtime
  with strict TypeScript and fails closed on errors. The compiler is pinned locally,
  and release CI installs it, checks the bindings and ships each package's `runtime/`.
  `tests/check-runtime.mjs` checks all binding targets/arity and the numeric-char index
  conversion exposed by strict checking; existing .NET parity probes cover behavior.
  Full SDK acceptance (including type-error failure cleanup), all four frontend
  builds and production four-worker real-WAD checks pass. The six GC geometry
  fixtures still match native .NET after extraction.
- Fixed boxed value reads through `IReadOnlyList<T>` in the browser rewrite.
  Transpose's interface accessor boxed array-backed enum/bool/numeric elements,
  breaking strict equality and GC's exact-source-normal classification. The
  regression in `tests/check-bcl.mjs` fails before the fix and passes afterwards.
  Regenerated all four frontend SDKs; ordered triangle positions match native
  .NET for GC classes 336, 2378, 2593, 2673, 2674 and Boldan 3322 (0x0CFA),
  including all 2,124 Boldan triangles. DAE exports are not a facing reference.
  The frontend SDK cache version was bumped to invalidate old converted maps.
- T01 through T04 complete. Current selected package adds unchanged BinaryMagic to
  UYA/GC level settings, BinarySpanReader and PifHeader: 17 methods plus constants.
- `tests/check-buffers.mjs` passes 395 .NET comparison results plus JS ownership/offset
  checks and strict TS consumers. It covers overlapping and failed copies, clear/fill,
  empty/default spans, array conversions, Index/Range values, compound/ref writes and
  lowered stack allocations, endian reads/writes, BitConverter overloads, numeric
  boundaries, float bit patterns and short buffers. The test runner checks subprocess
  launch errors explicitly.
- Discovery regression, the restored 17-method package's parser/integer/float and
  WAD/PIF detection checks, and its strict TypeScript consumer all pass.
- `tests/check-arithmetic.mjs` passes 70 .NET comparison results covering checked and
  unchecked 32/64-bit arithmetic, signed/unsigned conversions, overflow boundaries,
  division failures and evaluation order.
- `tests/check-numerics.mjs` passes 81 .NET comparison results for the discovered Vector2/3/4,
  Quaternion, Matrix4x4 and BitOperations surface. It checks transforms, axis direction,
  composition, float32 rounding, all quaternion-to-matrix branches, general/singular
  inversion, NaN/zero behavior and equality.
- T05a is complete. `tests/check-value-bindings.mjs` covers numeric inputs/results, nullable
  values, nested projections, primitive/buffer/numeric collections, copied mutable-list
  inputs, all numeric struct shapes, readable static properties, invalid inputs and
  strict TypeScript declarations. Numeric compound assignments are lowered correctly.
- T05b1 is complete. Plain TypeScript objects now become real transpiled C# domain
  instances through generated constructors. Recursive models, model collections,
  nullable models, constructor defaults, property-initialized option defaults and
  required members are covered; the probe also exercises the real `PackedFile` API.
- T05b2 is complete. The value probe covers map/set copying and validation, nested
  quaternion tracks, tuple buffers, enum values, byte-memory spans, 64-bit boundaries,
  and the real GC sky-rotation dictionary result.
- T05b3 is complete. Model projections retain their native instances, preserve live
  mutable state, round-trip opaque models without reconstructing their object graphs,
  and expose supported instance methods plus `createInstance`. Multi-out methods use
  labeled readonly tuples beginning with their success flag.
- T06a is complete. `Stream`, `MemoryStream`, `BinaryReader` and `BinaryWriter` inputs
  accept buffers or retained `MemoryStream` facades; writable results remain observable
  through `toArray`, and stream position/length/seek use `bigint`. `tests/check-io.mjs` verifies
  byte ownership, seeks, EOF, binary primitives and the real PIF/VIF stream APIs.
- T06b is complete. The synchronous callers retain their existing contracts through the
  frontend's pinned `fflate` dependency. The I/O probe covers real PNG zlib decoding,
  .NET-compatible truncated checksums, UYA custom ZIP extraction, and malformed,
  missing-entry, duplicate-entry and truncated-archive failures.
- T07a is complete. Public `JsonElement` inputs are recursive `JsonValue` structures;
  read-only DOM operations execute over copied native values, while stream/byte parsing
  uses native `JSON.parse` with fatal UTF-8 decoding. `tests/check-json.mjs` covers seven .NET
  glTF cases plus invalid SDK inputs and strict declarations.
- T07b is complete. Native JSON serialization covers bytes, indentation, enum strings,
  ignored members and mutable `JsonNode` graphs. `tests/check-json-serialization.mjs` compares
  object and mutation output byte-for-byte with .NET, including malformed inputs.
- T07c is complete. Shared compatibility covers the discovered path, buffer, span,
  collection, regex, encoding, SHA-256 and timing surface. SDK boundaries now project
  arbitrary metadata as `JsonValue`, type callbacks, and adapt `IMobyModelInput` and
  `IMobyModelOutput` to byte-oriented frontend objects. `tests/check-bcl.mjs` exercises these
  contracts, callback validation and deterministic ordering.
- Current full-library report succeeds: 396 public types, 241 compiled physical source
  files, 401 exported members, zero binding errors and zero compiler diagnostics. The
  full generated declaration file passes strict TypeScript checking.
- SDK inventory/cleanup hoists structural input normalization once per model, reducing
  the generated boundary module from 2.35 MB to 760 KB (213 KB to 67 KB gzip). The
  documented generator responsibilities and `npm run check` provide one sequential
  full acceptance path; probe-created TypeScript consumers are removed after checks.
- T08 is complete. `RatchetPs2.Sdk.FrontendMapPackageBuilder` owns cross-game WAD
  dispatch; game projects own target-specific and custom-map composition.
  Generated byte-oriented bindings retain gameplay files for the per-game readers;
  the deprecated WASM host now delegates map conversion to this library.
  `tests/check-entrypoints.mjs` covers the generated runtime and strict declarations.
- T09 is complete. `tests/check-uya-e2e.mjs` matches 1,873 standard-WAD entries and
  1,187 custom-map entries plus gameplay against .NET. Map-o-matic now loads,
  converts and parses UYA maps through the generated SDK; its production build
  and 26 test suites pass.
- Map-o-matic now loads, converts and parses RC1, GC and DL through independent
  game-specific TypeScript SDK workers. Every game uses the same four-way common,
  terrain, Moby and Tie split; its production-worker check converts and merges all
  four parts from real WADs, parses their gameplay, and parses a real DL mission.
  The frontend no longer imports, initializes or ships the WASM runtime. The check
  exposed and now covers native integer-span writes and numeric
  `char.ToLowerInvariant` emission.
- Map-o-matic now imports a two-root UYA dependency-closure build rather than the
  complete 401-export SDK. Runtime reflection metadata is disabled; the production
  SDK chunk fell from 7.0 MB / 890 KB gzip to 2.70 MB / 445 KB gzip. SDK loading is
  awaited in the loading stage, and its cached ES-module promise initializes once.
- UYA SDK evaluation, conversion and gameplay parsing now run in a dedicated module
  worker. The worker reports initialization timing and conversion/transfer/parse
  phases to the existing load-stage UI, and transfers packed output without copying.
- Profiling the representative 17.4 MB UYA WAD separates the ~90 ms module import
  from the asset conversion. Shared native byte copies, numeric reads/writes, WAD
  decompression and indexed texture decoding, direct C# loops in the hot Tie/Moby
  pipelines, uninitialized arrays where every element is overwritten, and direct
  lowering of little-endian byte reads reduced conversion from 96.7 s to about 13 s while preserving all
  1,873 WAD and 1,187 custom-map parity entries. The remaining work is the actual
  212-Moby/90-Tie/terrain glTF build, not SDK initialization.
- Hot Tie parsing now uses direct indexed passes, Moby serialization-only metadata
  uses shaped objects instead of per-field dictionaries, and geometric triangle keys
  allocate one canonical key instead of four strings plus a sorted array. Repeated
  single-thread runs are 12.6-13.9 s; the same native .NET build is 3.05 s.
- UYA asset conversion now runs common, terrain, Moby and Tie package groups in four
  persistent SDK workers and merges their packed output without changing render bytes
  or manifest content. The representative WAD completes in 5.6-6.0 s and the custom
  map in 5.1 s; Moby export is now the longest worker at 5.4-5.8 s.
- Map-o-matic scene setup now parses Tie, Shrub and Moby class glTF sources in a shared
  four-worker pool. Geometry buffers and decoded image bitmaps transfer back without
  copies; the UI thread only hydrates Three.js objects and builds render instances.
  Scene progress distinguishes worker source parsing from completed class builds.
- Model-family batches now use WebGPU storage-backed instance matrices. The
  representative level's 105 Tie chunks had 29 different counts, which previously
  produced fixed-size uniform-array shader variants; storage bindings make the shader
  layout size-independent and skip Three.js's overwritten identity-matrix fill.
- The TypeScript generator is now a first-class `src/RatchetPs2.TypeScriptSdk.Generator`
  project organized by discovery, transpilation, packaging and verification. The CLI,
  libraries, tests, WASM project and generator now share one .NET 10 SDK/target contract;
  map-o-matic imports the generated package from its promoted location.
- Next: finish T10 parity/browser checks for DL custom maps and missions plus the
  remaining reusable asset APIs, then wire clean-checkout generation into CI for T11.

## Verification commands

Run from this directory, using .NET 10 for the generator and the same NuGet cache
used at restore. Probes mutate a temporary Core source and share `bin/probe`, so
run them sequentially. All accept an optional .NET 10 host path argument.

```sh
npm run check # complete sequential acceptance suite
node tests/check-buffers.mjs
node tests/check-arithmetic.mjs
node tests/check-numerics.mjs
node tests/check-value-bindings.mjs
node tests/check-io.mjs
node tests/check-json.mjs
node tests/check-json-serialization.mjs
node tests/check-bcl.mjs
node tests/check-discovery.mjs
node tests/check-entrypoints.mjs # after a full SDK build
node tests/check-uya-e2e.mjs # real UYA WAD and custom ZIP
dotnet run --no-restore # builds the complete SDK and refreshes the full report
dotnet run --no-restore -- --type '*UyaLevelSettingsReader' --type '*GcLevelSettingsReader' --type '*BinarySpanReader' --type '*PifHeader' --type '*BinaryMagic' --verify
node tests/check.mjs
node ../../tools/ratchet-map-o-matic/node_modules/typescript/bin/tsc --noEmit --strict --target es2022 --module esnext --moduleResolution bundler tests/check.ts
```
