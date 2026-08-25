## ADDED Requirements

### Requirement: Define pack lifecycle scripts

The `pack.yml` schema SHALL allow an optional `scripts` mapping with at most one declaration for each `preInstall`, `postInstall`, `preUpdate`, and `postUpdate` hook. Each declaration SHALL select exactly one execution form: a non-empty pack-relative `file` with a non-empty `runner`, or a non-empty external `command`. Both forms MAY contain an ordered `arguments` array of strings and a non-empty `description`. The schema SHALL reject unknown hook names, unknown declaration properties, unsafe file paths, mixed execution forms, and incomplete declarations. Existing pack manifests that omit `scripts` SHALL remain valid.

#### Scenario: Validate a command that invokes a packed script

- **WHEN** a pack declares `scripts/setup.ps1` as a hook file and `pwsh` as its runner
- **THEN** the pack manifest is valid and preserves the file, runner, and ordered arguments

#### Scenario: Validate an inline tool command

- **WHEN** a pack declares an executable and arguments without shipping a script file
- **THEN** the pack manifest is valid

#### Scenario: Reject a mixed execution form

- **WHEN** one hook declaration contains both `file` and `command`
- **THEN** the pack manifest is invalid

#### Scenario: Reject an unsafe packed file

- **WHEN** a hook file is rooted or contains a parent traversal segment
- **THEN** the pack manifest is invalid

#### Scenario: Reject multiple scripts for one hook

- **WHEN** a pack represents one hook as a collection of script declarations
- **THEN** the pack manifest is invalid

#### Scenario: Preserve a manifest without scripts

- **WHEN** an existing valid pack manifest omits `scripts`
- **THEN** the pack manifest remains valid

### Requirement: Define lifecycle suppression on composite references

Each composite pack reference in `pack.yml` SHALL allow an optional `disabledHooks` collection containing unique values from `preInstall`, `postInstall`, `preUpdate`, and `postUpdate`. An omitted or empty collection SHALL suppress no hook. Existing composite references without `disabledHooks` SHALL remain valid.

#### Scenario: Disable selected hooks for a referenced pack

- **WHEN** a composite reference declares `preInstall` and `postInstall` in `disabledHooks`
- **THEN** the pack manifest is valid and preserves both suppressed lifecycle types

#### Scenario: Reject an unknown lifecycle type

- **WHEN** a composite reference declares an unsupported value in `disabledHooks`
- **THEN** the pack manifest is invalid

### Requirement: Represent named sources and project script trust in version-1 configuration

The `lunapack.yml` schema SHALL require every local, Git, and GitHub-derived source entry to contain a non-empty `name`. Source names SHALL be unique within one project configuration. The schema SHALL allow a project-scoped `trust` mapping containing unique source-name entries and unique pack entries. Each pack entry SHALL require a configured source name and bare pack ID without a version selector. Existing schema version `1` SHALL be retained.

#### Scenario: Validate named sources and trust

- **WHEN** a version-1 project configuration contains uniquely named sources, distinct trusted source names, and distinct source-plus-pack-ID entries
- **THEN** the project configuration is valid

#### Scenario: Reject duplicate source names

- **WHEN** two configured sources have the same ordinal name
- **THEN** the project configuration is invalid

#### Scenario: Reject version-specific pack trust

- **WHEN** a trusted pack entry contains an `@version` selector
- **THEN** the project configuration is invalid

#### Scenario: Reject pack trust without a source

- **WHEN** a trusted pack entry contains an ID but no configured source name
- **THEN** the project configuration is invalid

#### Scenario: Preserve empty trust collections

- **WHEN** a version-1 project configuration contains empty `trust.sources` and `trust.packs` collections
- **THEN** the project configuration is valid without a schema-version migration

### Requirement: Define cross-platform user trust settings

LunaPack SHALL define a user-settings document at `~/.lunapack/config.yml`. It SHALL contain optional global source and source-plus-pack-ID trust entries and optional local-project trust records keyed by canonical absolute project directory. A local-project record SHALL use the same trust shapes and MAY acknowledge project-scoped declarations by their exact source identities. Duplicate, incomplete, version-qualified, or unsafe project-path entries SHALL be invalid.

#### Scenario: Validate global and local-user trust

- **WHEN** user settings contain global trust and a local-project record keyed by a canonical project path
- **THEN** the settings are valid on the current operating system

#### Scenario: Reject a relative local-project key

- **WHEN** a local-project trust record uses a relative project path
- **THEN** the user settings are invalid

### Requirement: Persist immutable configured-source identity for every resolved pack

The existing `lunapack-lock.yml` schema version SHALL require every resolved root and transient pack to identify its configured source by name, type, and normalized location fields. Git provenance SHALL continue to include the resolved commit. The configured-source identity used for update and trust matching SHALL exclude mutable resolution data such as a Git commit while the lock record retains that data as release provenance.

#### Scenario: Lock source identity for a local transient pack

- **WHEN** installation resolves a transient pack from a named local source
- **THEN** its lock record contains the source name, local type, normalized configured path, and pack path

#### Scenario: Lock source identity and commit for a Git pack

- **WHEN** installation resolves a pack from a named Git source
- **THEN** its lock record contains the source name, normalized URL, ref and repository path when present, plus the resolved commit
