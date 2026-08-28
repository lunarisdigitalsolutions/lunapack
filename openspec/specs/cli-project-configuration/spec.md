# cli-project-configuration Specification

## Purpose

Define the initial project configuration workflow for a consumer that starts using LunaPack with a local pack source.

## Requirements

### Requirement: Initialize a LunaPack project manifest

The `luna init` command SHALL create `lunapack.yml` and `lunapack-lock.yml` in the current directory when neither exists. The configuration SHALL conform to the project-configuration schema with schema version `1`, empty `sources`, `packs`, `links`, and `variables` collections, and a `trust` mapping containing empty `sources` and `packs` collections. The lock file SHALL conform to its schema with an empty resolved pack graph and empty resolved link collection.

#### Scenario: Initialize an unconfigured directory

- **WHEN** a user runs `luna init` in a directory without `lunapack.yml` or `lunapack-lock.yml`
- **THEN** LunaPack creates schema-valid empty configuration and lock files, including empty link, source, pack, and trust collections

#### Scenario: Refuse to replace an existing manifest

- **WHEN** a user runs `luna init` in a directory that already contains `lunapack.yml` or `lunapack-lock.yml`
- **THEN** LunaPack leaves existing project state unchanged and returns a non-success result

### Requirement: Persist portable link definitions

`lunapack.yml` SHALL contain link definitions as project-owned intent separate
from resolved source commits, selected-file inventories, ownership, and content
digests. Commands that read and write project configuration SHALL preserve
schema-valid links they do not modify. Existing version-1 configurations that
omit `links` SHALL remain valid and SHALL be interpreted as having no links.

#### Scenario: Preserve links during an unrelated configuration change

- **WHEN** a user modifies a source, remapping, variable, trust entry, or requested pack through the CLI
- **THEN** LunaPack preserves every schema-valid link definition in `lunapack.yml`

#### Scenario: Read existing configuration without links

- **WHEN** LunaPack reads a valid version-1 project configuration that omits `links`
- **THEN** it treats the project as having no configured links without requiring migration

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

The `luna sources add git <name> <repository-url>` command SHALL add a named Git source to the current directory's `lunapack.yml`. It SHALL accept optional `--ref <branch-tag-or-commit>` and `--path <repository-relative-path>` arguments, canonicalize a supplied symbolic ref and safe base path before persistence, and reject duplicate names, embedded credentials, invalid or ambiguous refs, unsafe paths, or a fingerprint already configured under any name. A missing ref SHALL retain existing workspace remote-HEAD behavior. The command SHALL preserve existing configuration when arguments, source resolution, or project state are invalid.

#### Scenario: Add a Git source with a branch and path

- **WHEN** a user runs `luna sources add git shared-packs <repository-url> --ref main --path packs` after initialization and `main` resolves as a branch
- **THEN** `lunapack.yml` contains one Git source named `shared-packs` with the repository URL, `refs/heads/main`, and normalized `packs` path

#### Scenario: Add a Git source with defaults

- **WHEN** a user runs `luna sources add git shared-packs <repository-url>` after initialization
- **THEN** `lunapack.yml` contains one Git source named `shared-packs` with that repository URL and no explicit ref or path

#### Scenario: Reject a duplicate Git source

- **WHEN** a user adds a Git source whose normalized fingerprint equals an existing configured source under another name
- **THEN** LunaPack identifies the authoritative existing source and leaves project configuration unchanged

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

The `luna sources add git <name> <repository-url>` and `luna sources add github <name> <owner/repository>` commands SHALL require and persist a unique non-empty source name while retaining their ref, path, URL conversion, validation, and fingerprint-uniqueness behavior. GitHub shorthand SHALL accept exactly one owner and repository segment for `github.com`, reject hostnames and complete URLs, require `--ref`, and persist a normal Git source URL with a canonical ref. GitHub Enterprise repositories SHALL use the `git` variant.

#### Scenario: Add a named Git source

- **WHEN** a user runs `luna sources add git shared-packs <repository-url> --ref main --path packs`
- **THEN** `lunapack.yml` contains a Git source named `shared-packs` with the supplied URL, canonical ref, and normalized path

#### Scenario: Add a named GitHub source

- **WHEN** a user runs `luna sources add github public-packs lunarisdigitalsolutions/packs --ref main`
- **THEN** `lunapack.yml` contains a Git source named `public-packs` with URL `https://github.com/lunarisdigitalsolutions/packs.git` and ref `refs/heads/main`

#### Scenario: Reject invalid GitHub shorthand

- **WHEN** a user supplies a hostname, complete URL, missing ref, or value other than `owner/repository` to `luna sources add github`
- **THEN** LunaPack returns a non-success result without changing project configuration

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

`luna sources rm <name>` SHALL remove the source with that exact case-sensitive name only when `lunapack-lock.yml` records no installed pack or link consumer for it. In the same atomic project-state update, Luna SHALL remove project-scoped source and pack trust entries bound to that source name. It SHALL retain trust entries and configuration for all other sources and SHALL not automatically remap consumers.

#### Scenario: Remove a source while others remain

- **WHEN** a user removes an unconsumed configured source and at least one configured source remains
- **THEN** Luna persists the source and associated trust removal, confirms
  success, and recommends `luna sources list` and `luna discover`

#### Scenario: Remove the last source

- **WHEN** a user removes the only configured source and it has no lock-file consumers
- **THEN** Luna persists the source and associated trust removal, reports that
  no sources remain, and recommends `luna sources add git <name>
 <repository-url>`

#### Scenario: Remove a source used by an installed pack

- **WHEN** an installed pack or link lock record names the requested source
- **THEN** Luna identifies each consumer and returns a non-success result without changing configuration, trust, lock state, or managed files

### Requirement: Validate workspace source fingerprint uniqueness

Every operation that loads, validates, or writes `lunapack.yml`, including source addition, link addition, pack installation or update, and source rename, SHALL reject duplicate normalized source fingerprints. Fingerprint uniqueness SHALL be limited to the effective source collection in one `lunapack.yml`.

#### Scenario: Reject manually duplicated sources

- **WHEN** a workspace configuration contains differently named sources with the same normalized fingerprint
- **THEN** LunaPack identifies both names and returns a non-success result before changing configuration, files, or lock state

### Requirement: Rename a configured source atomically

`luna sources rename <current-id> <new-id>` SHALL require a valid unused identifier and SHALL atomically update the source key, lock-file source references, link references, and workspace-level references while preserving the source fingerprint. Pack manifests SHALL remain unchanged because they use pack-local aliases. When a manually renamed source has one unambiguous fingerprint match to prior lock state, LunaPack SHALL request explicit approval before repairing references; ambiguous changes SHALL require the rename command.

#### Scenario: Rename a consumed source

- **WHEN** a user renames a configured source to a valid unused identifier
- **THEN** LunaPack updates every authoritative workspace reference and preserves every pack-local alias in one project-state transaction

#### Scenario: Reject a colliding rename

- **WHEN** the requested new identifier already exists
- **THEN** LunaPack returns a non-success result without changing configuration or lock state

#### Scenario: Reject an unknown source name

- **WHEN** a user runs `luna sources rm <name>` for a name that is not
  configured
- **THEN** Luna returns a non-success result without changing configuration,
  trust, lock state, or managed files
