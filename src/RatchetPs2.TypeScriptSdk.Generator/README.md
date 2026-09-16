# C# to JavaScript / TypeScript SDK build

The build discovers the reusable C# libraries and generates JavaScript plus
TypeScript declarations. C# remains the source of truth. It uses Transpose's
JavaScript runtime, without WASM or Blazor.

The ordered implementation tasks and completion checks are in [PLAN.md](PLAN.md).

## Build

Requires the repository's .NET 10 SDK and Node.js. Run `npm ci` to install the
pinned TypeScript compiler and runtime dependency, and restore the library projects
before the first run. Every generation strictly type-checks and compiles the browser
runtime before emitting an importable SDK.

```sh
cd src/RatchetPs2.TypeScriptSdk.Generator
npm ci
dotnet run
```

With no selection, this attempts **all** public types in `RatchetPs2.Core`,
`RatchetPs2.Games.*`, and `RatchetPs2.Sdk`. The current full build discovers 396
public types and emits 401 SDK members; unsupported additions fail the build
rather than being omitted.

During migration, `--type` accepts repeatable, case-sensitive full-name patterns;
`--output` keeps independent dependency-closure builds in separate directories.
A verified subset is:

```sh
dotnet run -- --type '*UyaLevelSettingsReader' --type '*GcLevelSettingsReader' --type '*BinarySpanReader' --type '*PifHeader' --type '*BinaryMagic' --verify
node tests/check.mjs
```

Map-o-matic consumes narrow per-game frontend builds rather than the complete SDK:

```sh
npm run build:frontend-uya
npm run build:frontend-dl
npm run build:frontend-gc
npm run build:frontend-rc1
```

Each command selects only that game's package builder and gameplay readers, then
discovers their source dependency closure automatically. `--verify` writes .NET
comparison results for the parser checks.
`NUGET_PACKAGES`, if set during restore, must also be set when running because
Transpose resolves its BCL from the same cache. Package versions are pinned.
The generated package uses the pinned `fflate` dependency for the synchronous
zlib and ZIP operations required by the existing C# APIs.

## Current inventory

The successful full build currently contains:

| Surface | Count |
| --- | ---: |
| Source projects | 6 |
| Evaluated source files | 242 |
| Physical files transpiled | 241 |
| Public types inventoried | 396 |
| Exported members | 401 |
| API mapping errors | 0 |
| Compiler diagnostics | 0 |

`full-library-report.json` is the authoritative machine-readable inventory. The
unminified complete package is currently 6.55 MB of Transpose runtime/application
code, 776 KB of SDK boundary code, and 241 KB of declarations; gzip reduces those
to about 834 KB, 70 KB, and 25 KB. Runtime reflection metadata is disabled because
the SDK boundaries do not use it. Structural input normalizers are emitted once
per model rather than repeated in every exported method.

The frontend entrypoints select game-specific public roots and dependency closures.
Vite emits independent lazy workers of about 2.7 MB each, so loading one game does
not evaluate the other three. This is build-time reachability rather than bundler
tree shaking: Transpose emits a
side-effectful global script, so unused C# code must be excluded before compilation.

The generator is organized by responsibility:

- `Program.cs` composes discovery, transpilation, reporting, and output checks.
- `Discovery/ProjectSources.cs` evaluates MSBuild inputs and computes source dependencies.
- `Transpilation/BrowserSourceRewriter.cs` performs browser-safe Roslyn rewrites.
- `Transpilation/BrowserCompatibility.cs` supplies the browser BCL surface absent from Transpose.
- `Runtime/*.ts` implements native browser operations as strict TypeScript ES modules.
  Compatibility templates contain only `Browser.Type.Method({argument})` bindings;
  `BrowserSourceRewriter.cs` rewrites C# syntax, not JavaScript implementations.
  `Runtime/contracts.ts` describes the generated Transpose boundary without `any`.
  The emitted `runtime/` directory is part of each SDK package and must be shipped
  alongside `index.js` and `ratchetps2.js`. `npm run check:runtime` validates the
  implementations and all template targets/argument counts without the frontend.
- `Packaging/SdkPackage.cs` generates bindings, validation, projections, declarations, and
  the ES-module wrapper.
- `Verification/SdkVerification.cs` writes native .NET parity fixtures; `tests/` contains
  the JavaScript parity, declaration, discovery, and end-to-end acceptance checks.

## How discovery works

1. **MSBuild** evaluates each library's `Compile`, `ProjectReference`, `Using`,
   analyzer and reference-assembly items. This honors file exclusions and linked
   files instead of assuming every `.cs` file belongs to the build.
2. **Roslyn** binds the sources against the projects' .NET reference assemblies
   and runs their source generators, including Core's generated regex. For a
   selection, semantic references determine the dependency closure, including
   helper types and all partial declarations. Closure is conservative at file
   granularity: selecting one type includes the other declarations in its file.
3. **Reflection** on the native compilation discovers public static methods,
   constants, parameter signatures, nullability and result models. The generator
   creates C# forwarding methods, JavaScript entry points, object projections and
   TypeScript declarations from that metadata.
4. **Transpose** compiles those sources and forwarding methods. A shared Roslyn
   normalization step removes unused imports and nullable suppression, evaluates
   constant `sizeof`, lowers stack allocations, preserves float32 arithmetic,
   adapts byte-span views/copies/indexing and UTF-8 literals, and adapts
   `ArgumentNullException.ThrowIfNull` while retaining its argument name.
   `Transpilation/BrowserCompatibility.cs` supplies the missing binary, in-memory I/O, JSON, numerics,
   collection, path, regex, hashing and timing operations;
   Core's binary reader and geometry code need no transpiler-specific branches.
5. **Node** checks the emitted JavaScript syntax before the build succeeds.

There is no source-file list, per-method JavaScript wrapper, or copied TypeScript
model to update when adding a supported C# feature. New methods in selected types
and new types matched by a selection are discovered on the next run. Default
full-library builds discover all new public types.

The build evaluates the common `net10.0` target and combines the source
into one discovery assembly. Different project conditional-symbol sets, linked
sources with conflicting imports, and unmapped NuGet dependencies fail explicitly.
This is a repository build tool, not a general replacement for MSBuild.

## Consume

Successful output is the ES-module package in ignored `bin/probe/`:

```ts
import {
  RatchetPs2_Games_UYA_Gameplay_UyaLevelSettingsReader as uya,
  RatchetPs2_Games_GC_Gameplay_GcLevelSettingsReader as gc,
} from './bin/probe/index.js';

const settings = uya.read(bytes); // Uint8Array or ArrayBuffer
console.log(settings.shipPosition.x);
const optional = gc.tryRead(bytes); // GcLevelSettings | null
```

The complete package retains the cross-game composition entry point. Browser hosts
should select a game/feature-specific root instead; no WASM host is involved:

```ts
import {
  RatchetPs2_Core_Games_GameId as gameId,
  RatchetPs2_Sdk_FrontendMapPackageBuilder as maps,
} from './bin/probe/index.js';

const renderPackage = maps.buildLevelWad(wadBytes, gameId.uya);
const customMapPackage = maps.buildUyaCustomMapZip(zipBytes);
```

Map-o-matic imports the matching `bin/frontend-{game}/index.js`; each worker statically
references only its own generated package. For UYA:

```ts
import { RatchetPs2_Sdk_UyaFrontendMapPackageBuilder as maps } from './bin/frontend-uya/index.js';

const renderPackage = maps.buildLevelWad(wadBytes);
const customMapPackage = maps.buildCustomMapZip(zipBytes);
```

Map-o-matic loads each entrypoint inside a dedicated module worker. SDK evaluation,
WAD/custom-ZIP conversion and gameplay parsing therefore run outside the UI thread;
the worker reports each phase and transfers the large packed output buffer without
copying it back to the main realm. UYA divides its asset groups among four workers;
RC1, GC and DL use the same common, terrain, Moby and Tie worker split.

Export identifiers retain the full C# type name, with namespace separators
replaced by underscores. Methods and model fields use camelCase. Overloads get
parameter-type suffixes, such as `echo__Int32` and `echo__String`. Naming
collisions fail instead of overwriting an export.

Result models are plain objects, collections are native arrays, and byte arrays
are `Uint8Array`. Span results preserve shared storage: `Uint8Array` inputs stay
native views, and Transpose's internally allocated ordinary arrays use registered
indexed views. Generated `ByteSpan` declarations describe both; read-only spans
have read-only declarations. Pass these views back to span-taking APIs, or use
`Uint8Array.from(view)` for an owned copy. Arbitrary array-like inputs are rejected.
64-bit integer results use `bigint`. Optional parameters retain their defaults. A boolean method
with one final `out` parameter becomes a result-or-null function, as with `TryRead`.
Boolean methods with multiple trailing `out` parameters return a labeled readonly tuple
whose first item is the success flag, preserving failure details and default outputs.
Vector2/3/4, Quaternion and Matrix4x4 values use readonly structural objects with
camel-case components. Numeric and collection inputs are validated and copied at
the boundary, so methods that mutate a C# `List<T>` do not mutate the caller's array.
Constructible domain values accept generated structural input objects and are rebuilt
as native transpiled C# instances, recursively through nested values and collections.
Property-initialized options preserve their C# defaults when fields are omitted.
Projected domain models are branded facades retaining the native instance; live getters
and generated instance methods preserve state, and `createInstance` constructs a facade
directly. SDK-owned cyclic or non-constructible models can flow into later SDK calls
without flattening and rebuilding their object graphs.
Dictionary and set values use copied native `Map` and `Set` instances; value tuples
use readonly TypeScript tuples. Byte-backed `ReadOnlyMemory<byte>` results become owned
`Uint8Array` values, and signed/unsigned 64-bit inputs require range-checked `bigint`.
Readable static C# properties are exposed as readonly JavaScript accessors.
`JsonElement` inputs use a recursive `JsonValue` union and are validated and copied at
the SDK boundary. `JsonDocument` stream/byte parsing returns native JSON values and
rejects malformed JSON or invalid UTF-8. JSON serialization and mutable nodes preserve
the discovered .NET contracts for bytes, enum names, ignored members and indentation.
Stream-taking APIs accept `Uint8Array`, `ArrayBuffer`, or a retained `MemoryStream`.
Use `createMemoryStream()` for writable output, then inspect `position` and `length`,
call `seek(bigint)`, or retrieve an owned `Uint8Array` with `toArray()`.
BinaryReader/Writer parameters use the same stream contract and live position.
Callbacks have generated function signatures and validate their returned values.
`IMobyModelInput` and `IMobyModelOutput` use structural frontend objects whose methods
enumerate paths or read/write bytes, without requiring browser filesystem access.
Arbitrary metadata is recursively projected as `JsonValue`, never misleading `any`.

[Transpose currently emits global scripts](https://docs.curiosity.ai/transpose/core-concepts/output-types).
The package wraps them for ES-module loading but still installs the runtime and
compiled types on `globalThis`; use one SDK version per JavaScript realm.

## Diagnostics and verification

`bin/probe/report.json` records projects, source counts, selected types, dependency
files, generated export signatures, API mapping errors and compiler diagnostics.
The last full-library report is also kept as `full-library-report.json` when a
later selected build replaces `report.json`. `errors.txt` contains the complete
error text. Failed builds exit nonzero and remove stale SDK entry points.

Run the complete sequential acceptance suite with:

```sh
npm run check
```

Pass an explicit .NET host when needed with `npm run check -- /path/to/dotnet`.

`tests/check.mjs` compares generated JavaScript with .NET on ten UYA cases, GC parsing,
`TryRead`, malformed/truncated buffers, nonzero byte offsets, integer/float
boundaries, span views/copies and argument validation. TypeScript declarations
are checked with an installed compiler:

```sh
tsc --noEmit --strict --target es2022 --module esnext --moduleResolution bundler tests/check.ts
```

The discovery regression test temporarily creates new C# files, a split partial
helper, and an excluded invalid source. It builds, adds another public method,
rebuilds, and verifies exports and model types without editing the generator.
It also checks overloads, normalization and failed-build cleanup. It removes its
temporary sources in `finally`; rerun the selected build afterwards to restore
that SDK output:

```sh
node tests/check-discovery.mjs
```

You can pass a .NET 10 host path as the first argument to that test.

`node tests/check-buffers.mjs` creates a temporary C# probe and compares 395 results
against .NET for span creation, ranges, slicing, indexing, equality, null arrays,
bounds, named-argument/evaluation order and mutation. It also checks UTF-8, native
typed-array offsets, internal-array views, overlapping/failed copies, clear/fill,
empty/default spans, array conversions, Index/Range values, compound/ref writes,
stackalloc lowering, endian primitives, BitConverter overloads, signed boundaries,
float bit patterns, short buffers and strict TS view declarations (using map-o-matic's
installed TypeScript). It accepts the same optional .NET host path. These probes remove
their sources in `finally` and reject child-process launch failures; rerun the selected
build afterwards to restore its package.

`node tests/check-arithmetic.mjs` compares checked and unchecked 32/64-bit arithmetic,
signed/unsigned conversions, overflow and division failures, primitive boundaries
and operand evaluation order against .NET. It uses the same temporary-source and
optional .NET-host conventions as the buffer probe.

`node tests/check-numerics.mjs` compares 81 vector, quaternion, matrix and bit-operation
results with .NET. It covers transforms and handedness, composition, float32
rounding, quaternion conversion branches, general and singular inversion, NaN/zero
behavior, equality and numeric compound assignments, using the same temporary-source
conventions.

`node tests/check-value-bindings.mjs` checks numeric and recursive domain inputs/results,
nullable values, primitive/buffer/model collections, maps, sets, tuples, enums, byte
memory, bigint boundaries, option defaults, required fields, the real `PackedFile` and
GC sky-rotation APIs, retained mutable/opaque object lifetimes, single/multi-out results,
static properties, validation, copied-input mutation behavior and strict TypeScript
declarations.

`node tests/check-io.mjs` compares in-memory Stream and BinaryReader/Writer serialization with
.NET, including copied inputs, seek/position/length behavior, EOF failures, real VIF/PIF
operations, PNG zlib decoding, UYA ZIP extraction, malformed/truncated inputs and strict
TypeScript declarations.

`node tests/check-json.mjs` compares native JSON parsing and glTF inspection with .NET for
complete, missing, null, malformed and invalid-UTF-8 inputs. It also checks recursive SDK
boundary validation and strict `JsonValue` TypeScript declarations.

`node tests/check-json-serialization.mjs` compares serialization and mutable-node behavior with
.NET, including deferred LINQ enumerables. `node tests/check-bcl.mjs` covers hashing, paths, sorting, regex, spans, queues, typed
callbacks, arbitrary metadata and the byte-oriented Moby input/output interfaces.

`node tests/check-uya-e2e.mjs` builds the narrow UYA package and compares a real standard WAD
and custom-map ZIP with .NET, including package entries, JSON, binary render attributes
and gameplay. Map-o-matic's production-worker integration check also converts real RC1,
GC and DL WADs through their generated packages. All frontend load and gameplay routes
now use the TypeScript SDK; representative browser performance remains part of T12 acceptance.
