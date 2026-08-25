## ADDED Requirements

### Requirement: Remap managed-file targets during installation

LunaPack SHALL resolve a managed file from its manifest-declared target to an
effective project-relative target before preflight and mutation. `luna install`
SHALL accept repeatable `--remap-directory <declared-directory>=<target-directory>`
and `--remap-file <declared-file>=<target-file>` options. Global mappings in
`lunapack.yml` SHALL apply to installations that do not have a higher-precedence
command-line mapping. Exact file mappings SHALL take precedence over directory
mappings; command-line mappings of the same kind SHALL take precedence over
global mappings. A directory mapping SHALL retain the matched descendant suffix.

Every declared and effective target supplied by remapping SHALL be non-empty,
project-relative, and contained within the project directory. LunaPack SHALL
reject invalid mappings, duplicate mappings of the same scope and source, and
target ownership or filesystem conflicts before changing project files,
configuration, or lock state. `--destination` SHALL not be combined with either
remapping option.

#### Scenario: Remap a manifest target directory at installation

- **WHEN** a consumer runs `luna install madr-adr-template --remap-directory docs/adr=docs/internal/01-architecture/decisions`
- **THEN** LunaPack writes `docs/internal/01-architecture/decisions/template.md`
  and records that effective target as managed by `madr-adr-template`

#### Scenario: Rename a single managed file at installation

- **WHEN** a consumer runs `luna install madr-adr-template --remap-file docs/adr/template.md=docs/adr/_template.md`
- **THEN** LunaPack writes and records `docs/adr/_template.md` as the managed
  target for the declared `docs/adr/template.md` file

#### Scenario: Retain descendants under a mapped directory

- **WHEN** a directory mapping redirects `docs/adr` and a pack manages
  `docs/adr/records/template.md`
- **THEN** LunaPack retains `records/template.md` beneath the configured
  effective directory

#### Scenario: Reject ambiguous destination and remapping options

- **WHEN** a consumer supplies `--destination` together with a remapping option
- **THEN** LunaPack returns a non-success result without changing project files,
  configuration, or lock state

### Requirement: Preserve effective ownership during lifecycle operations

LunaPack SHALL use the effective managed-file targets recorded in
`lunapack-lock.yml` for updates and uninstalls. An update SHALL apply retained
managed files at their recorded effective targets and SHALL apply project-level
remapping only to newly introduced declared targets. Changing global remapping
after installation SHALL not relocate an already managed file; consumers SHALL
use `luna mv` to relocate it explicitly.

#### Scenario: Update a remapped managed file

- **WHEN** an installed pack has a remapped managed target and a later selected
  release changes that managed file
- **THEN** `luna update` applies its strategy at the recorded remapped target

#### Scenario: Uninstall a remapped managed file

- **WHEN** an installed remapped managed file still matches its recorded digest
  and the consumer runs `luna uninstall` for its pack
- **THEN** LunaPack removes the file from its recorded remapped target

#### Scenario: Do not relocate an existing managed file after configuration change

- **WHEN** a consumer changes a global remap after the matching managed file is
  already installed and then updates its pack
- **THEN** LunaPack retains the existing recorded target instead of moving it

### Requirement: Move a managed file while retaining pack ownership

LunaPack SHALL provide `luna mv <source> <target>` to relocate exactly one
lock-recorded managed file. Both paths SHALL be non-empty project-relative paths
contained within the project directory. The command SHALL reject a source that
is not a uniquely owned lock target, a target already owned by another managed
file, or a state where both source and target files exist. When the source file
exists and the target does not, LunaPack SHALL move the file and update its lock
record atomically. When the source file does not exist but the target file
exists, LunaPack SHALL update only the matching lock record to adopt the target
path, retaining its recorded digest for later lifecycle protection.

#### Scenario: Move an installed ADR template

- **WHEN** `docs/adr/template.md` is a uniquely owned managed file and a user
  runs `luna mv docs/adr/template.md docs/architecture/adr/_template.md`
- **THEN** LunaPack moves the file to the target and records the target as the
  managed file location

#### Scenario: Rebind ownership to an already moved file

- **WHEN** a lock-recorded source is absent, its requested target exists, and a
  user runs `luna mv` with those paths
- **THEN** LunaPack leaves project files unchanged and updates the managed-file
  lock record to the target path

#### Scenario: Refuse an ambiguous move

- **WHEN** both the lock-recorded source and requested target files exist
- **THEN** LunaPack returns a non-success result without moving files or
  changing lock state
