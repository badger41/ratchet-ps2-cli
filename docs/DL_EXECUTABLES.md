# Deadlocked executable export

`map export-executables` reconstructs the retail US Deadlocked boot executable
and level overlays as MIPS ELF files, and writes a `config.ini` for repeatable
boot-ELF ISO builds. It uses the existing DL level/segment readers and WAD
decompressor. Existing map extraction commands are unchanged.

```sh
dotnet run --project src/RatchetPs2.Cli -- map export-executables \
  --game DL --input /path/to/deadlocked.iso --output exports/dl-executables
```

The output directory must be empty (an empty `.gitkeep` file is allowed). Input recognition currently requires
`SCUS-97465`, revision `1.00`; other regions and prototypes are not qualified.
All executable files are constructed before any output is written, so an
unsupported record layout does not leave a partially converted executable set.

## Output layout

The 48 executable files use numeric level IDs and the CLI's `code/` directory convention:

```text
boot.elf
config.ini
levels/
  0000/code/overlay.elf
  0001/code/overlay.elf
  ...
  0074/code/overlay.elf
```

Use `map extract --game DL --level 1 --input deadlocked.iso --output levels/0001`
to extract a level's normal CLI asset package beside `code/overlay.elf`.
Executable export itself does not generate model, texture, or gameplay files.
There is no dependency on external asset-bank metadata or named planet folders.
This workflow does not repack edited level overlays, models, textures, or gameplay.

`boot.elf` is the original reference executable. The compiler output can have any
name or location: `config.ini` selects it independently. A level overlay is an ELF;
an asset-system "underlay" is metadata, not another executable to reconstruct.

## Config-driven ISO builds

Export can select a compiler output and ISO destination immediately:

```sh
ratchet-ps2 map export-executables --game DL --input deadlocked.iso \
  --output assets/dl --boot-elf dl/build/boot_elf.elf --output-iso dl/build/new_dl_cli.iso
```

Command-line paths are relative to the shell's working directory. Generated
`config.ini` paths are relative to the configuration file. The `--boot-elf` path
may point to a file that compilation will create later. Without these optional
arguments, the config uses the exported `boot.elf` and a local `new_dl.iso`.

For an existing ratchet-project extraction, add `assets/dl/config.ini` without
re-exporting or moving any files:

```ini
[build]
source_iso=/absolute/path/to/deadlocked.iso
boot_elf=../../dl/build/boot_elf.elf
output_iso=../../dl/build/new_dl_cli.iso
```

Values are literal paths, with no quotes or environment-variable expansion.
Spaces and `=` are supported within paths; comment lines begin with `;` or `#`.
Only the three documented keys in `[build]` are accepted. Windows absolute paths
also work when running the CLI on Windows; use paths available to the host where
the CLI runs. Keep local ISO paths out of commits.

After compiling, run from any directory:

```sh
ratchet-ps2 map build-boot --config /path/to/assets/dl/config.ini
```

Each build reads the current ELF at `boot_elf`; no manual copying is required.
The builder validates the ELF load ranges and entry point, copies the original
disc, appends the ELF, updates its ISO directory extent/length and volume size,
and verifies the installed ELF bytes. Existing game assets and sector locations
are preserved. The output is built in a temporary file and replaces the previous
output only after successful verification. The source ISO is never patched in
place. This currently supports US retail 1.00 without supplementary directory
trees. It validates the ISO payload, not emulator boot or gameplay.

The `ratchet-project` integration script `src/games/tools/build_dl_cli.ps1` runs
its existing Docker `make -j8 elf` command, then this config-driven packer on the
host. `-SkipCompile` packages an already-built ELF. Initialize the usual compiler
and split output first; do not overlap compilation with another build in the
same game directory. The script accepts `-Config` and `-CliRepository` overrides.

## Library placement

- `Core/Disc/Iso9660RootReader`: bounded ISO root-file reads from a seekable stream.
- `Core/Executables`: game-neutral MIPS ELF output models and binary writer.
- `Games.DL/Executables`: DL executable records, section metadata, and ISO composition.
- `Games.DL/Builders/DlBootIsoBuilder`: boot replacement in a copied ISO.
- `Cli/Abstractions/BootBuildConfiguration`: host-side INI paths.
- `Cli/Handlers/DlExecutableExportHandler`: export orchestration.
- `Cli/Commands/Map/MapExportExecutablesCommand`: command options and reporting.

The C# implementation
uses binary format fields and the observable reference output, with the existing
CLI readers providing the payloads. No game binary fixtures are distributed.

DL's copy records include payload bytes even for sections marked NOBITS. The
export preserves those bytes in load segments. `patch.data` is represented as
PROGBITS for compatibility with GNU MIPS tools, while `.reginfo` retains its MIPS
section type. ELF entry points, section names, addresses, flags, and segment
contents are retained. Unknown record counts/types and inconsistent entry points
fail explicitly instead of producing a guessed executable layout.

## Validation

Synthetic checks (no game files required):

```sh
dotnet run --project tests/RatchetPs2.LevelTests -- --dl-executables
```

Optional comparison against a local reference extraction (native or historical layout):

```sh
dotnet run --project tests/RatchetPs2.LevelTests -- \
  --qualify-dl-executables /path/to/deadlocked.iso /path/to/reference/assets/dl
```

This requires all 48 reference ELFs and compares the entire file, including
headers and payload bytes. The initial US retail qualification matched all 48
files exactly. The native output paths differ from the historical reference layout.
This proves executable reconstruction parity, not ISO rebuilding or gameplay.

Boot ISO regression checks:

```sh
dotnet run --project tests/RatchetPs2.LevelTests -- --dl-boot-iso
dotnet run --project tests/RatchetPs2.LevelTests -- --verify-dl-boot-iso \
  /path/to/original.iso /path/to/rebuilt.iso /path/to/compiled/boot_elf.elf
```

The full-image check compares every original ISO byte, allowing changes only to
the boot directory extent/size and primary volume size. It also verifies the
appended ELF and both-endian volume size. The real US retail ISO passed this check
with a compiled ratchet-project ELF, including through the project script with
`-SkipCompile`. Compilation and emulator gameplay were not rerun in that check.

The initial validation also passed the CLI build (zero warnings/errors), synthetic
malformed-record checks, and CLI refusal checks for existing output, unsupported
games, and unsupported disc regions. The broader LevelTests suite stopped at
`ValidateUyaLevelArchiveBuilder` with `UYA SDK archive replacements should survive
final compression and reader re-entry`. The same failure was reproduced using
`--uya-sdk-archive` in a clean snapshot of the upstream commit; the full suite
cannot be reported as passing.
