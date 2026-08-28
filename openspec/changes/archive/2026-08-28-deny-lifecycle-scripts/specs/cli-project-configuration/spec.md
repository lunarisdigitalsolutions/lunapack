# cli-project-configuration Delta Specification

## ADDED Requirements

### Requirement: Persist blanket script denial in explicit scopes

LunaPack SHALL support a `deny.scripts` boolean policy in project-scoped trust, global-user trust, and project-local user trust. `luna trust scripts deny` SHALL set the policy to `true` and `luna trust scripts reset` SHALL remove it. Both commands SHALL support mutually exclusive `--project` and `--global` options and SHALL default to project-local user scope when neither option is present. Setting denial SHALL require no confirmation because it removes execution authority. Resetting denial SHALL show the selected scope, warn that retained source or pack grants can become effective again, require interactive confirmation, and fail closed without mutation when confirmation is declined or unavailable.

Project-scoped denial SHALL be portable in `lunapack.yml` and SHALL apply without a user acknowledgement because it cannot grant execution authority. Global-user and project-local user denial SHALL be stored atomically in `~/.lunapack/config.yml`. Source and pack grants SHALL remain stored while denial is active. `luna trust list` SHALL identify script denial in the selected scope independently from retained grants.

#### Scenario: Deny scripts for the current project user

- **WHEN** a user runs `luna trust scripts deny` without a scope option
- **THEN** LunaPack records script denial for that user and canonical project path without requesting confirmation

#### Scenario: Deny scripts in portable project configuration

- **WHEN** a user runs `luna trust scripts deny --project`
- **THEN** `lunapack.yml` contains `trust.deny.scripts: true` without creating a user acknowledgement

#### Scenario: Deny scripts globally

- **WHEN** a user runs `luna trust scripts deny --global`
- **THEN** the global-user trust record contains `deny.scripts: true` and applies across that user's projects

#### Scenario: List denial and retained grants

- **WHEN** a selected trust scope contains script denial and source or pack grants
- **THEN** `luna trust list` reports both the denial policy and every retained grant

#### Scenario: Reset denial after warning

- **WHEN** a user confirms `luna trust scripts reset` for a scope containing script denial
- **THEN** LunaPack removes only that scope's denial policy and preserves its source and pack grants

#### Scenario: Reject non-interactive denial reset

- **WHEN** `luna trust scripts reset` cannot obtain interactive confirmation
- **THEN** LunaPack returns a non-success result without changing trust settings

## MODIFIED Requirements

### Requirement: Initialize a LunaPack project manifest

The `luna init` command SHALL create `lunapack.yml` and `lunapack-lock.yml` in the current directory when neither exists. Each generated document SHALL conform to its version-1 schema and contain only schema-required properties. The project configuration SHALL contain schema version `1` and empty required `sources` and `packs` collections. The lock file SHALL contain schema version `1` and an empty required `packs` collection. Optional empty or default-valued links, remapping, trust, variables, and denial properties SHALL be omitted.

#### Scenario: Initialize an unconfigured directory

- **WHEN** a user runs `luna init` in a directory without `lunapack.yml` or `lunapack-lock.yml`
- **THEN** LunaPack creates schema-valid project and lock files containing only their required version and empty pack or source properties

#### Scenario: Refuse to replace an existing manifest

- **WHEN** a user runs `luna init` in a directory that already contains `lunapack.yml` or `lunapack-lock.yml`
- **THEN** LunaPack leaves existing project state unchanged and returns a non-success result

### Requirement: Initialize and preserve project variables

LunaPack SHALL interpret an omitted `variables` mapping as an empty collection and SHALL preserve schema-valid project variables while reading and writing configuration for lifecycle commands. `luna init` SHALL omit the optional mapping when no variables are configured.

#### Scenario: Initialize a project with variables support

- **WHEN** a user runs `luna init` in an unconfigured directory
- **THEN** the created `lunapack.yml` omits the optional `variables` mapping

#### Scenario: Preserve configured variables during installation

- **WHEN** a project containing schema-valid variables installs a pack
- **THEN** LunaPack retains the variables in `lunapack.yml`
