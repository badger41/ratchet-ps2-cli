# AI / Contributor Steering Notes

This file documents the intended architectural contract for future contributors and AI agents working in this repository.

## Primary architectural intent

This repository is intended to support:

1. a cross-platform CLI
2. reusable SDK-style library consumption from other .NET projects
3. a generated TypeScript SDK for browser applications and workers

Because of that, contributors should preserve a clean separation between:

- CLI concerns
- shared reusable logic
- game-specific logic

## Project responsibilities

### `RatchetPs2.Cli`

Use this project for:

- command parsing
- command routing
- CLI help text
- stdout/stderr messaging
- user-facing export/output choices
- host-specific orchestration

Do **not** put core domain logic here unless it is truly CLI-only.

#### Internal CLI structure

Within `RatchetPs2.Cli`:

- `Commands/` should contain only actual command definitions and command group definitions
- shared CLI helpers such as common options, command builders, or reusable command wiring should live outside `Commands/` (for example in `Abstractions/`)
- `Program.cs` should remain a thin composition root that wires dependencies and registers commands

When adding a shared CLI argument or option that will be reused across multiple commands, prefer adding it to the shared CLI helper area rather than duplicating it in individual command files.

### `RatchetPs2.Core`

Use this project for:

- shared domain models
- cross-game abstractions
- reusable parsing/transformation logic
- APIs intended for non-CLI consumers

Anything in this project should be safe to consume from tests, other .NET apps, and the generated TypeScript SDK.

### `RatchetPs2.Games.RC1`, `GC`, `UYA`, `DL`

Use these projects for:

- game-specific constants
- game-specific models
- game-specific service implementations
- quirks or version-specific behavior

Do not move something to `Core` unless it is meaningfully shared.

### `RatchetPs2.Sdk`

Use this project only for host-independent workflows that compose Core with more
than one game library. It may expose byte-oriented frontend entry points, but must
not depend on the CLI, browser APIs, or filesystem-only orchestration.

### `RatchetPs2.TypeScriptSdk.Generator`

Use this project for SDK discovery, browser-target Roslyn rewrites, TypeScript
package generation, and parity probes. Keep transpiler compatibility code here,
not in Core or the game libraries.

## Placement rules

When adding a new type or service:

- if it is CLI presentation or command UX, place it in `RatchetPs2.Cli`
- if it is shared across multiple games, place it in `RatchetPs2.Core`
- if it is specific to one game, place it in that game project

If uncertain, prefer keeping something game-specific first and promote it to `Core` later when reuse is clearly established.

## SDK-friendly rules

To preserve library usability:

- avoid direct console I/O in reusable code
- avoid file-path-only APIs when streams or byte-oriented APIs are possible
- avoid OS-specific APIs in shared libraries
- avoid static mutable global state
- prefer explicit models and service abstractions

## Browser SDK rules

To preserve TypeScript SDK usability:

- do not assume unrestricted filesystem access
- do not require native platform interop in core logic
- be careful about memory-heavy APIs for large assets
- avoid tying reusable code to host-specific runtime behavior

## Dependency direction

Preferred dependency direction:

```text
RatchetPs2.Cli -> RatchetPs2.Core
RatchetPs2.Cli -> RatchetPs2.Games.*
RatchetPs2.Games.* -> RatchetPs2.Core
RatchetPs2.Sdk -> RatchetPs2.Core + RatchetPs2.Games.*
```

Avoid:

- `RatchetPs2.Core -> RatchetPs2.Cli`
- `RatchetPs2.Games.* -> RatchetPs2.Cli`
- unnecessary dependencies between game projects

## Current naming conventions

- Game IDs use abbreviated uppercase names: `RC1`, `GC`, `UYA`, `DL`
- Per-game project names should match that abbreviation style
- Commands should remain clearly separated from non-command infrastructure
- Public `Core` APIs must use game-neutral names, inputs, outputs, and diagnostics; do not remove a game prefix while retaining game-specific assumptions
- Game-specific archive and disc implementations belong under `RatchetPs2.Games.<GAME>/Builders` and retain that game's prefix
- Public SDK entry points stay game-neutral and dispatch to the target implementation using `GameId` or equivalent target metadata
- A per-game frontend builder may remain public in its game project when the TypeScript generator needs it as a package root; desktop consumers should still use the neutral SDK facade

## Implementation preference

When possible, implement reusable capabilities in library projects first, then have the CLI call into them.

The CLI should stay thin.
