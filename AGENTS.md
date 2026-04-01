# AI / Contributor Steering Notes

This file documents the intended architectural contract for future contributors and AI agents working in this repository.

## Primary architectural intent

This repository is intended to support:

1. a cross-platform CLI
2. reusable SDK-style library consumption from other .NET projects
3. possible future WASM-oriented hosts

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

### `RatchetPs2.Core`

Use this project for:

- shared domain models
- cross-game abstractions
- reusable parsing/transformation logic
- APIs intended for non-CLI consumers

Anything in this project should ideally be safe to consume from tests, other apps, and future WASM-friendly hosts.

### `RatchetPs2.Games.RC1`, `GC`, `UYA`, `DL`

Use these projects for:

- game-specific constants
- game-specific models
- game-specific service implementations
- quirks or version-specific behavior

Do not move something to `Core` unless it is meaningfully shared.

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

## WASM-friendly rules

To preserve future WASM compatibility:

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
```

Avoid:

- `RatchetPs2.Core -> RatchetPs2.Cli`
- `RatchetPs2.Games.* -> RatchetPs2.Cli`
- unnecessary dependencies between game projects

## Current naming conventions

- Game IDs use abbreviated uppercase names: `RC1`, `GC`, `UYA`, `DL`
- Per-game project names should match that abbreviation style
- Commands should remain clearly separated from non-command infrastructure

## Implementation preference

When possible, implement reusable capabilities in library projects first, then have the CLI call into them.

The CLI should stay thin.