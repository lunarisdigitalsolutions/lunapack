## ADDED Requirements

### Requirement: Configure global managed-target remapping

`lunapack.yml` SHALL support an optional `remap` mapping with optional
`directories` and `files` mappings. Each mapping key SHALL be a manifest-declared
project-relative target path and each mapping value SHALL be a non-empty
project-relative destination path that resolves within the project directory.
A directory mapping SHALL match the complete declared directory or a descendant,
and a file mapping SHALL match only its exact declared file target. Invalid,
absolute, or escaping paths SHALL make the project configuration invalid.

#### Scenario: Configure an ADR directory mapping

- **WHEN** `lunapack.yml` maps `docs/adr` to
  `docs/internal/01-architecture/decisions` in `remap.directories`
- **THEN** the configuration is valid and makes that mapping available to pack
  lifecycle commands

#### Scenario: Reject an unsafe remap destination

- **WHEN** a global remap destination is absolute or resolves outside the
  project directory
- **THEN** LunaPack rejects the configuration without changing project files or
  lock state

#### Scenario: Normalize Windows-style configuration paths

- **WHEN** project configuration uses `\` as a separator in a source,
  destination, or remapping path
- **THEN** LunaPack accepts the path and writes it to project configuration and
  lock state with `/` separators

#### Scenario: Prefer an exact file mapping

- **WHEN** global remapping declares both a directory mapping for `docs/adr`
  and a file mapping for `docs/adr/template.md`
- **THEN** the exact file mapping takes precedence for `docs/adr/template.md`

### Requirement: Manage global remapping through the CLI

`luna remap set <directory|file> <target> <newTarget>` SHALL validate and
normalize both paths as project-relative paths, then create or replace the
matching entry in `remap.directories` or `remap.files` in `lunapack.yml`. The
command SHALL retain mappings in the other scope and reject unsupported scopes
or unsafe paths without changing project configuration or lock state. `luna
remap list` SHALL render all configured mappings. `luna remap rm
<directory|file> <target>` SHALL remove the matching mapping and omit an empty
`remap` configuration. Updating global remapping SHALL NOT relocate an already
installed managed file.

#### Scenario: Configure a directory mapping through the CLI

- **WHEN** a consumer runs
  `luna remap set directory docs/adr docs/internal/01-architecture/decisions`
- **THEN** `lunapack.yml` records the normalized mapping in `remap.directories`

#### Scenario: Configure an exact file mapping through the CLI

- **WHEN** a consumer runs
  `luna remap set file docs/adr/template.md docs/adr/_template.md`
- **THEN** `lunapack.yml` records the normalized mapping in `remap.files`
  without removing directory mappings

#### Scenario: Reject an invalid CLI remapping

- **WHEN** a consumer supplies an unsupported scope or a path that escapes the
  project directory
- **THEN** LunaPack returns a non-success result without changing project
  configuration or lock state
