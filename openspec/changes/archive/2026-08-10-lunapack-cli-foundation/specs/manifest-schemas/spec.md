## Purpose

Define machine-readable contracts for the initial LunaPack project configuration and local pack manifest files.

## ADDED Requirements

### Requirement: Publish project-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `lunapack.yml`. The schema SHALL require schema version `1`, define `sources` as local-source entries with paths, and define installed-pack records including identity, version, source, managed target paths, and content digests. It SHALL reject unsupported source types and unknown required-state omissions.

#### Scenario: Validate an initialized manifest

- **WHEN** the schema validates a manifest created by `lunapack init`
- **THEN** validation succeeds

#### Scenario: Reject an unsupported source type

- **WHEN** the schema validates a manifest containing a non-local source type
- **THEN** validation fails

### Requirement: Publish local pack-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `pack.yml`. The schema SHALL require a pack identity, semantic version, and a non-empty managed-file declaration with source and target paths.

#### Scenario: Validate the dotnet gitignore pack manifest

- **WHEN** the schema validates the repository's `dotnet-gitignore` pack manifest
- **THEN** validation succeeds

#### Scenario: Reject an incomplete pack manifest

- **WHEN** the schema validates a pack manifest without a version or managed-file declaration
- **THEN** validation fails

### Requirement: Maintain schema compatibility deliberately

The initial schemas SHALL use an explicit schema version and reject unsupported versions. Future incompatible project-manifest changes SHALL use a new version rather than silently reinterpret version `1` documents.

#### Scenario: Reject an unknown schema version

- **WHEN** the project-manifest schema validates a manifest with a schema version other than `1`
- **THEN** validation fails
