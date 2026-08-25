# manifest-schemas Specification

## Purpose

Define machine-readable contracts for LunaPack project configuration, lock state, and local pack manifests.

## Requirements

### Requirement: Publish project-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `lunapack.yml`. The schema SHALL require schema version `1`, define local and Git source entries, and define requested root pack references. A Git source SHALL require a repository URL and SHALL allow optional `ref`, optional repository-relative `path`, and optional `timeoutSeconds` from 1 through 300. Requested root pack references SHALL include an ID and MAY include an explicit Semantic Version request. The schema SHALL reject absolute local source paths, unsafe Git source paths, unsupported source types, resolved source provenance, managed file ownership, digests, and unknown required-state omissions. Existing valid local-source configuration SHALL remain valid.

#### Scenario: Validate an initialized manifest

- **WHEN** the schema validates a manifest created by `luna init`
- **THEN** validation succeeds

#### Scenario: Validate a Git source

- **WHEN** the schema validates a Git source with a repository URL and optional valid ref, path, and timeout
- **THEN** validation succeeds

#### Scenario: Reject an unsupported source type

- **WHEN** the schema validates a manifest containing a non-local source type
- **THEN** validation fails

#### Scenario: Reject an unsafe Git source path

- **WHEN** the schema validates a Git source path that is absolute or escapes the repository root
- **THEN** validation fails

#### Scenario: Reject an absolute local source path

- **WHEN** the schema validates a local source path rooted at a filesystem drive, UNC location, or root directory
- **THEN** validation fails

#### Scenario: Reject resolved installation state in configuration

- **WHEN** the schema validates `lunapack.yml` containing a resolved source path, managed-file list, or content digest
- **THEN** validation fails

### Requirement: Publish project lock-file schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `lunapack-lock.yml`. The schema SHALL require its explicit schema version and a resolved pack graph with exact pack identity and version, source provenance, composite references, and managed target-path SHA-256 records. Git-sourced pack provenance SHALL record the repository URL, requested ref when configured, configured repository path when configured, and the resolved commit SHA. It SHALL reject unknown lock schema versions and incomplete resolved pack records. Existing valid local-source lock records SHALL remain valid.

#### Scenario: Validate resolved composite lock state

- **WHEN** the lock schema validates the state produced for a composite pack and its transitive packs
- **THEN** validation succeeds

#### Scenario: Validate Git-resolved lock state

- **WHEN** the lock schema validates a Git-sourced pack record with its repository URL and resolved commit SHA
- **THEN** validation succeeds

#### Scenario: Reject incomplete resolved state

- **WHEN** the lock schema validates a resolved pack record without source provenance, an exact version, or a required managed-file digest
- **THEN** validation fails

#### Scenario: Reject Git provenance without a resolved commit

- **WHEN** the lock schema validates a Git-sourced pack record without a resolved commit SHA
- **THEN** validation fails

### Requirement: Publish local pack-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `pack.yml`. The schema SHALL require a pack identity, semantic version, non-empty license, and non-empty author, and allow an optional human-readable package description and up to 15 unique, non-empty tags. A pack SHALL declare one or more managed-file entries, one or more composite pack references, or both. Each composite reference SHALL contain a pack ID and an exact Semantic Version and MAY bind identifier-named string or boolean parameters for its referenced pack. Managed-file selectors MAY set `template` to opt into Scriban parsing; it defaults to false. Pack manifests SHALL not contain source configuration.

#### Scenario: Reject a pack manifest without attribution

- **WHEN** the schema validates a pack manifest without a license or author
- **THEN** validation fails

#### Scenario: Preserve manifests without a description

- **WHEN** the schema validates an existing complete pack manifest without a description
- **THEN** validation succeeds

#### Scenario: Reject an incomplete pack manifest

- **WHEN** the schema validates a pack manifest without a version or managed-file declaration
- **THEN** validation fails

#### Scenario: Validate the dotnet gitignore pack manifest

- **WHEN** the schema validates the repository's `dotnet-gitignore` pack manifest
- **THEN** validation succeeds

#### Scenario: Validate a manifest with a description

- **WHEN** the schema validates a pack manifest with a description and a managed-file declaration or composite pack reference
- **THEN** validation succeeds

#### Scenario: Validate bounded pack tags

- **WHEN** the schema validates a pack manifest with up to 15 unique, non-empty tags
- **THEN** validation succeeds

#### Scenario: Reject excessive pack tags

- **WHEN** the schema validates a pack manifest with more than 15 tags
- **THEN** validation fails

#### Scenario: Preserve file-only manifests

- **WHEN** the schema validates an existing complete pack manifest that declares managed files but no composite references
- **THEN** validation succeeds

#### Scenario: Validate a contentless composite manifest

- **WHEN** the schema validates a pack manifest with one or more composite references and no managed files
- **THEN** validation succeeds

#### Scenario: Reject an incomplete or unpinned composite reference

- **WHEN** the schema validates a pack manifest without a managed-file or composite declaration, or with a composite reference lacking an exact version
- **THEN** validation fails

#### Scenario: Validate composite reference parameter bindings

- **WHEN** a composite reference binds identifier-named string or boolean parameters
- **THEN** the pack manifest is valid

#### Scenario: Reject a source declaration in a pack manifest

- **WHEN** the schema validates a pack manifest containing source configuration
- **THEN** validation fails

#### Scenario: Preserve a managed file without template parsing

- **WHEN** a managed-file selector omits `template`
- **THEN** the manifest is valid and the selector defaults to non-template handling

### Requirement: Maintain schema compatibility deliberately

The project configuration schema SHALL retain explicit schema version `1`, and the lock-file schema SHALL use its own explicit schema version. LunaPack SHALL not support the former version-1 document shape that contains resolved source provenance or managed-file ownership. Future incompatible lock-file changes SHALL use a new lock-file schema version.

#### Scenario: Reject an unknown schema version

- **WHEN** either schema validates a document with an unsupported schema version
- **THEN** validation fails

#### Scenario: Reject a former combined-state manifest

- **WHEN** LunaPack reads a version-1 `lunapack.yml` that contains resolved source provenance, managed-file ownership, or content digests
- **THEN** it rejects the document as invalid project configuration

### Requirement: Represent optional pack destinations in version-1 state

The project-configuration and lock-file schemas SHALL allow an optional,
non-empty, project-relative `destination` for directly requested packs. The
lock-file schema SHALL allow the corresponding resolved destination while
retaining every effective managed target path and digest. Existing valid
version-1 state files that omit destination metadata SHALL remain valid.

#### Scenario: Validate destination-installed pack state

- **WHEN** the schemas validate state written after a destination-installed
  pack succeeds
- **THEN** the project configuration and lock file both validate and retain the
  requested destination

#### Scenario: Validate existing state without a destination

- **WHEN** the schemas validate a pre-destination version-1 configuration and
  lock file
- **THEN** validation succeeds without a schema-version migration

#### Scenario: Reject an unsafe persisted destination

- **WHEN** either schema validates an absolute destination or one that escapes
  the project root
- **THEN** validation fails

### Requirement: Define typed pack parameters

The `pack.yml` schema SHALL allow an optional `parameters` mapping keyed by a
non-empty parameter name. Each parameter declaration SHALL require a `type` of
`string`, `bool`, or `enum`; its `required` flag SHALL default to false. An
`enum` declaration SHALL contain a non-empty, unique collection of allowed
string `values`; other parameter types SHALL reject `values`. A parameter MAY
declare non-empty `displayName` and `description` strings for interactive
prompts. Existing valid version-1 pack manifests without parameters SHALL
remain valid.

#### Scenario: Validate an enum parameter declaration

- **WHEN** schema validation receives a parameter with type `enum`, a required
  flag, and distinct allowed string values
- **THEN** the pack manifest is valid

#### Scenario: Validate parameter display metadata

- **WHEN** a parameter declaration contains display name and description text
- **THEN** the pack manifest is valid

#### Scenario: Reject an unconstrained enum declaration

- **WHEN** schema validation receives an enum parameter without values or with
  duplicated values
- **THEN** the pack manifest is invalid

### Requirement: Define conditional managed files

The `pack.yml` schema SHALL allow an optional string `condition` on each
managed-file declaration. Existing manifests that omit `condition` SHALL
remain valid.

#### Scenario: Validate a managed file without a condition

- **WHEN** schema validation receives an existing managed-file declaration
  without a condition
- **THEN** the pack manifest is valid

### Requirement: Define project variables

The `lunapack.yml` schema SHALL allow an optional `variables` mapping whose
non-empty names map to string or boolean values. Existing valid version-1
project configuration without variables SHALL remain valid.

#### Scenario: Validate configured template variables

- **WHEN** schema validation receives project configuration with string and
  boolean variable values
- **THEN** the project manifest is valid

#### Scenario: Reject a non-scalar project variable

- **WHEN** schema validation receives a project variable whose value is not a
  string or boolean
- **THEN** the project manifest is invalid

### Requirement: Define pack lifecycle scripts

The `pack.yml` schema SHALL allow an optional `scripts` mapping with at most one declaration for each `preInstall`, `postInstall`, `preUpdate`, and `postUpdate` hook. Each declaration SHALL select exactly one execution form: a non-empty pack-relative `file` with a non-empty `runner`, or a non-empty external `command`. Both forms MAY contain an ordered `arguments` array of strings and a non-empty `description`. The schema SHALL reject unknown hook names, unknown declaration properties, unsafe file paths, mixed execution forms, and incomplete declarations. Existing pack manifests that omit `scripts` SHALL remain valid.

#### Scenario: Validate a command that invokes a packed script

- **WHEN** a pack declares `scripts/setup.ps1` as a hook file and `pwsh` as its runner
- **THEN** the pack manifest is valid and preserves the file, runner, and ordered arguments

#### Scenario: Reject a mixed execution form

- **WHEN** one hook declaration contains both `file` and `command`
- **THEN** the pack manifest is invalid

#### Scenario: Validate an inline tool command

- **WHEN** a pack declares an executable and arguments without shipping a script file
- **THEN** the pack manifest is valid

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

#### Scenario: Reject version-specific pack trust

- **WHEN** a trusted pack entry contains an `@version` selector
- **THEN** the project configuration is invalid

#### Scenario: Reject duplicate source names

- **WHEN** two configured sources have the same ordinal name
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
