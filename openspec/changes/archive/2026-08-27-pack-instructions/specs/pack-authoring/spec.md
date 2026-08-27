## ADDED Requirements

### Requirement: Author ordered typed lifecycle hooks

The CLI SHALL let authors list, append, replace, and remove typed hooks for `preInstall`, `postInstall`, `preUpdate`, and `postUpdate`. `luna pack add hook script command <event> <command> [arguments...]` SHALL append a command-form script, `luna pack add hook script file <event> <file> <runner> [arguments...]` SHALL append a file-form script, and `luna pack add hook instruction <event> <file>` SHALL append an instruction with optional `--templating`. Add commands SHALL accept `--replace <position>` to replace the hook at a one-based event position instead of appending. `luna pack hooks` SHALL list hooks in event and declaration order with one-based positions. `luna pack rm hook <event> <position>` SHALL remove exactly one positioned hook. The CLI SHALL preserve safe pack-relative paths and SHALL not execute or display hooks while authoring them.

#### Scenario: Append a command-form script hook

- **WHEN** an author runs `luna pack add hook script command postInstall npm install`
- **THEN** LunaPack appends a `script` hook that stores `npm` as `command` and `install` as its first argument

#### Scenario: Append a templated instruction hook

- **WHEN** an author runs `luna pack add hook instruction preInstall instructions/setup.md --templating`
- **THEN** LunaPack appends an `instruction` hook with the normalized file and enabled templating

#### Scenario: Replace a positioned hook

- **WHEN** an author adds a hook with `--replace 2` for an event containing at least two hooks
- **THEN** LunaPack replaces only the second hook and preserves the order of every other declaration

#### Scenario: Remove a positioned hook

- **WHEN** an author runs `luna pack rm hook postInstall 1`
- **THEN** LunaPack removes only the first `postInstall` hook

#### Scenario: List ordered hooks

- **WHEN** an author runs `luna pack hooks`
- **THEN** LunaPack lists each typed hook in lifecycle-event and declaration order with its one-based event position

## REMOVED Requirements

### Requirement: Author lifecycle scripts

**Reason**: Script-specific commands cannot author the unified ordered list of script and instruction hooks.

**Migration**: Replace `luna pack add script ...`, `luna pack rm script ...`, and `luna pack scripts` with the corresponding `luna pack add hook script ...`, `luna pack rm hook ...`, and `luna pack hooks` commands.
