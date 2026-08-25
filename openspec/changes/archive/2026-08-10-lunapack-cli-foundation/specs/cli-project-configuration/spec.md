## Purpose

Define the initial project configuration workflow for a consumer that starts using LunaPack with a local pack source.

## ADDED Requirements

### Requirement: Initialize a LunaPack project manifest

The `lunapack init` command SHALL create `lunapack.yml` in the current directory when no manifest exists. The created document SHALL conform to the project-manifest schema and contain schema version `1`, an empty `sources` collection, and an empty `packs` collection.

#### Scenario: Initialize an unconfigured directory

- **WHEN** a user runs `lunapack init` in a directory without `lunapack.yml`
- **THEN** LunaPack creates a schema-valid `lunapack.yml` with empty source and pack collections

#### Scenario: Refuse to replace an existing manifest

- **WHEN** a user runs `lunapack init` in a directory that already contains `lunapack.yml`
- **THEN** LunaPack leaves the existing file unchanged and returns a non-success result

### Requirement: Register a local pack source

The `lunapack source add local <path>` command SHALL add an existing local directory to the `sources` collection in the current directory's `lunapack.yml`. The recorded source SHALL identify its type as `local` and retain the supplied path. LunaPack SHALL not add a duplicate source path.

#### Scenario: Add an existing local source

- **WHEN** a user runs `lunapack source add local <path>` for an existing directory after initialization
- **THEN** `lunapack.yml` contains one local source with that path

#### Scenario: Reject an unavailable source path

- **WHEN** a user adds a local source whose path does not exist or is not a directory
- **THEN** LunaPack leaves `lunapack.yml` unchanged and returns a non-success result

#### Scenario: Reject a duplicate local source

- **WHEN** a user adds a local source path already recorded in `lunapack.yml`
- **THEN** LunaPack leaves `lunapack.yml` unchanged and returns a non-success result

### Requirement: Require valid project configuration

Commands that modify project configuration or packs SHALL require a schema-valid `lunapack.yml` in the current directory and SHALL return a non-success result without modifying project files when it is missing or invalid.

#### Scenario: Run a lifecycle command without a manifest

- **WHEN** a user runs `lunapack source add`, `lunapack install`, or `lunapack uninstall` without `lunapack.yml`
- **THEN** LunaPack reports the missing configuration and does not create or remove project content
