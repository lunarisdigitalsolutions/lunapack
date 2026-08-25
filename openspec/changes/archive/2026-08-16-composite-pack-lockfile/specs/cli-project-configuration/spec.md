## MODIFIED Requirements

### Requirement: Initialize a LunaPack project manifest

The `lunapack init` command SHALL create `lunapack.yml` and `lunapack-lock.yml` in the
current directory when neither exists. The configuration SHALL conform to the
project-configuration schema with schema version `1`, an empty `sources`
collection, and an empty requested-root `packs` collection. The lock file SHALL
conform to its schema with an empty resolved pack graph.

#### Scenario: Initialize an unconfigured directory

- **WHEN** a user runs `lunapack init` in a directory without `lunapack.yml` or
  `lunapack-lock.yml`
- **THEN** LunaPack creates schema-valid empty configuration and lock files

#### Scenario: Refuse to replace an existing manifest

- **WHEN** a user runs `lunapack init` in a directory that already contains
  `lunapack.yml` or `lunapack-lock.yml`
- **THEN** LunaPack leaves existing project state unchanged and returns a
  non-success result

### Requirement: Register a local pack source

The `lunapack source add local <path>` command SHALL add an existing local
directory to the `sources` collection in the current directory's `lunapack.yml`.
The recorded source SHALL identify its type as `local` and retain a path
relative to the project directory. LunaPack SHALL reject absolute source paths
and SHALL not add a duplicate source path.

#### Scenario: Add an existing local source

- **WHEN** a user runs `lunapack source add local <relative-path>` for an existing
  directory after initialization
- **THEN** `lunapack.yml` contains one local source with that relative path

#### Scenario: Reject an unavailable source path

- **WHEN** a user adds a local source whose relative path does not exist or is
  not a directory
- **THEN** LunaPack leaves project configuration unchanged and returns a
  non-success result

#### Scenario: Reject an absolute source path

- **WHEN** a user runs `lunapack source add local <absolute-path>`
- **THEN** LunaPack leaves project configuration unchanged and returns a
  non-success result

#### Scenario: Reject a duplicate local source

- **WHEN** a user adds a local source path already recorded in `lunapack.yml`
- **THEN** LunaPack leaves project configuration unchanged and returns a
  non-success result

### Requirement: Require valid project configuration

Commands that modify project configuration or packs SHALL require
schema-valid `lunapack.yml` and `lunapack-lock.yml` in the current directory.
They SHALL return a non-success result without modifying project files when
required state is missing or invalid.

#### Scenario: Run a lifecycle command without a manifest

- **WHEN** a user runs `lunapack source add`, `lunapack install`, or `lunapack uninstall`
  without valid configuration and lock state
- **THEN** LunaPack reports the missing or invalid state and does not create or
  remove project content
