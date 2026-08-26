# cli-project-configuration Specification

## Purpose

Define the initial project configuration workflow for a consumer that starts using LunaPack with a local pack source.

## Requirements

### Requirement: Initialize a LunaPack project manifest

The `luna init` command SHALL create `lunapack.yml` and `lunapack-lock.yml` in the current directory when neither exists. The configuration SHALL conform to the project-configuration schema with schema version `1`, empty `sources`, `packs`, and `variables` collections, and a `trust` mapping containing empty `sources` and `packs` collections. The lock file SHALL conform to its schema with an empty resolved pack graph.

#### Scenario: Initialize an unconfigured directory

- **WHEN** a user runs `luna init` in a directory without `lunapack.yml` or `lunapack-lock.yml`
- **THEN** LunaPack creates schema-valid empty configuration and lock files, including empty source and pack trust collections

#### Scenario: Refuse to replace an existing manifest

- **WHEN** a user runs `luna init` in a directory that already contains `lunapack.yml` or `lunapack-lock.yml`
- **THEN** LunaPack leaves existing project state unchanged and returns a non-success result

### Requirement: Register a local pack source

The `luna sources add local <name> <path>` command SHALL add an existing local directory to the `sources` collection in the current directory's `lunapack.yml`. The recorded source SHALL contain the supplied unique name, identify its type as `local`, and retain a path relative to the project directory. LunaPack SHALL reject empty or duplicate source names, absolute source paths, unavailable directories, and duplicate local source paths.

#### Scenario: Add an existing local source

- **WHEN** a user runs `luna sources add local engineering-packs <relative-path>` for an existing directory after initialization
- **THEN** `lunapack.yml` contains one local source named `engineering-packs` with that relative path

#### Scenario: Reject an unavailable source path

- **WHEN** a user adds a local source whose relative path does not exist or is not a directory
- **THEN** LunaPack leaves project configuration unchanged and returns a non-success result

#### Scenario: Reject an absolute source path

- **WHEN** a user runs `luna sources add local engineering-packs <absolute-path>`
- **THEN** LunaPack leaves project configuration unchanged and returns a non-success result

#### Scenario: Reject a duplicate local source

- **WHEN** a user adds a local source whose name or path is already recorded in `lunapack.yml`
- **THEN** LunaPack leaves project configuration unchanged and returns a non-success result

### Requirement: Register a Git pack source

The `luna sources add git <name> <repository-url>` command SHALL add a named Git source to the current directory's `lunapack.yml`. It SHALL accept optional `--ref <branch-or-commit>` and `--path <repository-relative-path>` arguments, retain the supplied values in the source entry, and reject duplicate names or a duplicate Git source with the same repository URL, ref, and path. It SHALL preserve existing configuration when the command arguments or project state are invalid.

#### Scenario: Add a Git source with a branch and path

- **WHEN** a user runs `luna sources add git shared-packs <repository-url> --ref main --path packs` after initialization
- **THEN** `lunapack.yml` contains one Git source named `shared-packs` with the repository URL, `main` ref, and `packs` path

#### Scenario: Add a Git source with defaults

- **WHEN** a user runs `luna sources add git shared-packs <repository-url>` after initialization
- **THEN** `lunapack.yml` contains one Git source named `shared-packs` with that repository URL and no explicit ref or path

#### Scenario: Reject a duplicate Git source

- **WHEN** a user adds a Git source whose repository URL, ref, and path equal an existing configured Git source
- **THEN** LunaPack leaves project configuration unchanged and returns a non-success result

### Requirement: Configure global managed-target remapping

`lunapack.yml` SHALL support an optional `remap` mapping with optional `directories` and `files` mappings. Each mapping key SHALL be a manifest-declared project-relative target path and each mapping value SHALL be a non-empty project-relative destination path that resolves within the project directory. A directory mapping SHALL match the complete declared directory or a descendant, and a file mapping SHALL match only its exact declared file target. Invalid, absolute, or escaping paths SHALL make the project configuration invalid.

#### Scenario: Configure an ADR directory mapping

- **WHEN** `lunapack.yml` maps `docs/adr` to `docs/internal/01-architecture/decisions` in `remap.directories`
- **THEN** the configuration is valid and makes that mapping available to pack lifecycle commands

#### Scenario: Reject an unsafe remap destination

- **WHEN** a global remap destination is absolute or resolves outside the project directory
- **THEN** LunaPack rejects the configuration without changing project files or lock state

#### Scenario: Normalize Windows-style configuration paths

- **WHEN** project configuration uses `\` as a separator in a source, destination, or remapping path
- **THEN** LunaPack accepts the path and writes it to project configuration and lock state with `/` separators

#### Scenario: Prefer an exact file mapping

- **WHEN** global remapping declares both a directory mapping for `docs/adr` and a file mapping for `docs/adr/template.md`
- **THEN** the exact file mapping takes precedence for `docs/adr/template.md`

### Requirement: Manage global remapping through the CLI

`luna remap set <directory|file> <target> <newTarget>` SHALL validate and normalize both paths as project-relative paths, then create or replace the matching entry in `remap.directories` or `remap.files` in `lunapack.yml`. The command SHALL retain mappings in the other scope and reject unsupported scopes or unsafe paths without changing project configuration or lock state. `luna remap list` SHALL render all configured mappings. `luna remap rm <directory|file> <target>` SHALL remove the matching mapping and omit an empty `remap` configuration. Updating global remapping SHALL NOT relocate an already installed managed file.

#### Scenario: Configure a directory mapping through the CLI

- **WHEN** a consumer runs `luna remap set directory docs/adr docs/internal/01-architecture/decisions`
- **THEN** `lunapack.yml` records the normalized mapping in `remap.directories`

#### Scenario: Configure an exact file mapping through the CLI

- **WHEN** a consumer runs `luna remap set file docs/adr/template.md docs/adr/_template.md`
- **THEN** `lunapack.yml` records the normalized mapping in `remap.files` without removing directory mappings

#### Scenario: Reject an invalid CLI remapping

- **WHEN** a consumer supplies an unsupported scope or a path that escapes the project directory
- **THEN** LunaPack returns a non-success result without changing project configuration or lock state

### Requirement: Require valid project configuration

Commands that modify project configuration or packs SHALL require schema-valid `lunapack.yml` and `lunapack-lock.yml` in the current directory. They SHALL return a non-success result without modifying project files when required state is missing or invalid.

#### Scenario: Run a lifecycle command without a manifest

- **WHEN** a user runs `luna sources add`, `luna install`, or `luna uninstall` without valid configuration and lock state
- **THEN** LunaPack reports the missing or invalid state and does not create or remove project content

### Requirement: Initialize and preserve project variables

LunaPack SHALL initialize `lunapack.yml` with an empty `variables` mapping and SHALL
preserve schema-valid project variables while reading and writing configuration
for lifecycle commands.

#### Scenario: Initialize a project with variables support

- **WHEN** a user runs `luna init` in an unconfigured directory
- **THEN** the created `lunapack.yml` contains an empty `variables` mapping

#### Scenario: Preserve configured variables during installation

- **WHEN** a project containing schema-valid variables installs a pack
- **THEN** LunaPack retains the variables in `lunapack.yml`

### Requirement: List configured source names

`luna sources list` SHALL include each source's name, type, and existing type-specific location details in configured precedence order.

#### Scenario: List local and Git source names

- **WHEN** a project contains named local and Git sources and the user runs `luna sources list`
- **THEN** each result shows its source name together with its type and path or repository details

### Requirement: Register named remote pack sources

The `luna sources add git <name> <repository-url>` and `luna sources add github <name> <organization/repository>` commands SHALL require and persist a unique non-empty source name while retaining their existing ref, path, URL conversion, validation, and duplicate-source behavior.

#### Scenario: Add a named Git source

- **WHEN** a user runs `luna sources add git shared-packs <repository-url> --ref main --path packs`
- **THEN** `lunapack.yml` contains a Git source named `shared-packs` with the supplied URL, ref, and path

#### Scenario: Add a named GitHub source

- **WHEN** a user runs `luna sources add github public-packs lunarisdigitalsolutions/packs`
- **THEN** `lunapack.yml` contains a Git source named `public-packs` with the derived GitHub URL

#### Scenario: Reject a reused source name

- **WHEN** a user adds a remote source using the name of any configured source
- **THEN** LunaPack returns a non-success result without changing project configuration

### Requirement: Persist warning-gated script trust in explicit scopes

The `luna trust source <name>...` command SHALL accept one or more configured source names. `luna trust pack <id>... --source <name>` SHALL accept one or more bare pack IDs and bind each to the exact identity of the named configured source. Both commands SHALL support mutually exclusive `--project` and `--global` options. With neither option, trust SHALL default to local-user scope for the current canonical project directory.

Local-user and global-user trust SHALL be persisted atomically in `~/.lunapack/config.yml`; global-user trust SHALL apply to every project for that user. Project-scoped trust SHALL be persisted in `lunapack.yml`, but SHALL become effective only when the current user's settings contain an acknowledgement bound to the same canonical project directory and exact source identities. The trust command SHALL create that acknowledgement in the same confirmed operation. A project file copied or modified without this user acknowledgement SHALL not authorize scripts.

Before any trust mutation, LunaPack SHALL display a danger message explaining that trusted scripts run with the user's permissions and may exploit repository or source compromise, future pack versions, credentials, filesystem and network access, dependencies, or irreversible external side effects. LunaPack SHALL show the exact scope and source identities and require interactive confirmation. It SHALL fail closed when confirmation is declined or unavailable. It SHALL validate the complete invocation before writing either file, use ordinal trust matching, add no duplicates, and preserve unrelated settings and project configuration.

#### Scenario: Trust multiple configured sources

- **WHEN** a user confirms `luna trust source local-packs shared-packs` and both names identify configured sources
- **THEN** both exact source identities are present once in the current project's local-user trust record

#### Scenario: Trust multiple bare pack IDs

- **WHEN** a user confirms `luna trust pack dotnet-sdk dotnet-quality --source shared-packs`
- **THEN** both source-plus-pack-ID entries are present once in the current project's local-user trust record

#### Scenario: Persist project trust with user acknowledgement

- **WHEN** a user confirms a trust command with `--project`
- **THEN** LunaPack writes the declaration to `lunapack.yml` and a matching acknowledgement to that user's local-project settings

#### Scenario: Ignore unacknowledged project trust

- **WHEN** a project contains trust declarations that the current user has not acknowledged for the exact project path and source identities
- **THEN** LunaPack treats those declarations as untrusted

#### Scenario: Persist global-user trust

- **WHEN** a user confirms a trust command with `--global`
- **THEN** LunaPack writes the exact source identities or source-plus-pack-ID entries to global user settings for all projects

#### Scenario: Show danger before trusting

- **WHEN** a trust invocation is valid and would mutate trust
- **THEN** LunaPack shows the attack vectors, exact scope, and identities and writes nothing until the user confirms

#### Scenario: Reject version-specific trust

- **WHEN** a user runs `luna trust pack dotnet-sdk@2.0.0 --source shared-packs`
- **THEN** LunaPack returns a non-success result and leaves project trust unchanged

#### Scenario: Reject an unknown source atomically

- **WHEN** one value in a multi-source trust command does not identify a configured source
- **THEN** LunaPack returns a non-success result without adding or acknowledging any value from that invocation

#### Scenario: Reject non-interactive trust creation

- **WHEN** a trust command cannot obtain interactive confirmation
- **THEN** LunaPack returns a non-success result without modifying project or user settings

### Requirement: Remove a configured source safely

`luna sources rm <name>` SHALL remove the source with that exact case-sensitive
name. In the same atomic project-state update, Luna SHALL remove project-scoped
source and pack trust entries bound to that source name. It SHALL retain
requested packs, resolved lock records, managed files, and trust entries for all
other sources.

#### Scenario: Remove a source while others remain

- **WHEN** a user removes a configured source and at least one configured source
 remains
- **THEN** Luna persists the source and associated trust removal, confirms
 success, and recommends `luna sources list` and `luna discover`

#### Scenario: Remove the last source

- **WHEN** a user removes the only configured source
- **THEN** Luna persists the source and associated trust removal, reports that
 no sources remain, and recommends `luna sources add git <name>
 <repository-url>`

#### Scenario: Remove a source used by an installed pack

- **WHEN** the removed source is named by an installed pack's resolved lock
 record
- **THEN** Luna retains the requested pack, managed files, and immutable lock
 evidence while removing the configured source and associated trust

#### Scenario: Reject an unknown source name

- **WHEN** a user runs `luna sources rm <name>` for a name that is not
 configured
- **THEN** Luna returns a non-success result without changing configuration,
 trust, lock state, or managed files
