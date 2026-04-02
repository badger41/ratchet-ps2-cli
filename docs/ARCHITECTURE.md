# Architecture

## Goals

This repository is being structured so it can serve three roles over time:

1. A cross-platform CLI
2. A reusable SDK/dependency for other .NET projects
3. A codebase that can later be adapted to WASM-friendly hosts

Because of that, the architecture should keep domain logic separate from host-specific behavior.

## Project roles

### `RatchetPs2.Cli`

This project is the command-line frontend.

It is responsible for:

- parsing command arguments
- resolving the selected game
- presenting output to stdout/stderr
- handling CLI-specific UX
- handling CLI-specific export/options concepts

It should **not** become the home of core parsing, game logic, or reusable data models.

#### Recommended internal CLI organization

Within the CLI project itself:

- keep `Program.cs` thin and focused on composition/root command registration
- keep `Commands/` reserved for actual commands and command groups
- place shared CLI-only helpers such as common options, command builders, and reusable command wiring outside `Commands/` (for example in `Abstractions/`)

If multiple commands share the same option or argument pattern, prefer defining it once in the shared CLI helper area and reusing it from command files.

### `RatchetPs2.Core`

This project is the shared library layer.

It should contain:

- shared models used across multiple games
- shared abstractions/contracts
- reusable logic that is not tied to the CLI
- APIs intended to be safe for SDK-style consumption

Anything placed here should be usable by:

- the CLI
- tests
- another .NET application
- a future WASM host

### `RatchetPs2.Games.RC1`, `GC`, `UYA`, `DL`

These projects isolate game-specific behavior.

They should contain:

- game-specific constants
- game-specific models that do not generalize well
- per-game readers/parsers/transformers
- implementations of shared contracts from `RatchetPs2.Core`

If a type or behavior is truly shared, it should be moved to `RatchetPs2.Core`.

## Contract for SDK-friendly code

Reusable code should follow these rules:

### 1. Keep host concerns out of reusable libraries

Library projects should not directly depend on:

- console input/output
- process launching
- shell commands
- OS-specific APIs
- UI frameworks

Those concerns belong in the CLI or another host layer.

### 2. Prefer data/stream-based APIs over file-path-only APIs

When possible, prefer inputs like:

- `Stream`
- `ReadOnlyMemory<byte>`
- `byte[]`
- plain option/model objects

This makes APIs easier to use in:

- CLI tools
- unit tests
- web apps
- WASM environments

### 3. Keep core logic deterministic and side-effect-light

Parsing and transformation logic should be pure or close to pure where practical.

Avoid hidden global state and avoid writing directly to console from reusable code.

### 4. Use public APIs intentionally

Types in `Core` intended for external use should have stable, understandable API shapes.

Prefer:

- explicit model types
- focused service abstractions
- clear exceptions/messages

Avoid exposing CLI-specific types from reusable projects.

### 5. WASM compatibility should remain possible

To preserve future WASM support, avoid baking in assumptions about:

- unrestricted filesystem access
- unrestricted threading/background workers
- native platform interop
- infinite memory for large asset processing

WASM support does not need to be fully implemented now, but new reusable APIs should avoid blocking it unnecessarily.

## Recommended dependency direction

Dependencies should generally point inward like this:

```text
RatchetPs2.Cli -> RatchetPs2.Core
RatchetPs2.Cli -> RatchetPs2.Games.*
RatchetPs2.Games.* -> RatchetPs2.Core
```

Avoid reverse dependencies such as:

- `Core -> Cli`
- `Games.* -> Cli`
- one game project depending on another game project unless there is a very strong reason

## Shared vs game-specific placement rule

When deciding where something belongs:

- put it in a game project if it only matches one game or has game-specific quirks
- put it in `Core` if it is meaningfully shared across multiple games
- keep it in `Cli` if it only exists for command-line interaction or presentation

If unsure, prefer starting in the game project and only promoting to `Core` once reuse is proven.

## Practical next-step API direction

As the codebase grows, favor adding reusable services in the library layer that the CLI simply calls.

Example direction:

- readers/parsers that accept streams or bytes
- export services that return reusable models or byte content
- game modules that expose capabilities through shared interfaces

The CLI should stay as thin orchestration over those reusable APIs.