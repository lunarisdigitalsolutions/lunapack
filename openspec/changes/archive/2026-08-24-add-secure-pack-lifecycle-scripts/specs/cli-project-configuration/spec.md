## MODIFIED Requirements

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

- **WHEN** a user adds a named local source whose relative path does not exist or is not a directory
- **THEN** LunaPack leaves project configuration unchanged and returns a non-success result

#### Scenario: Reject an absolute source path

- **WHEN** a user runs `luna sources add local engineering-packs <absolute-path>`
- **THEN** LunaPack leaves project configuration unchanged and returns a non-success result

#### Scenario: Reject a duplicate local source

- **WHEN** a user adds a local source whose name or path is already recorded in `lunapack.yml`
- **THEN** LunaPack leaves project configuration unchanged and returns a non-success result

## ADDED Requirements

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

### Requirement: List configured source names

`luna sources list` SHALL include each source's name, type, and existing type-specific location details in configured precedence order.

#### Scenario: List local and Git source names

- **WHEN** a project contains named local and Git sources and the user runs `luna sources list`
- **THEN** each result shows its source name together with its type and path or repository details

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
