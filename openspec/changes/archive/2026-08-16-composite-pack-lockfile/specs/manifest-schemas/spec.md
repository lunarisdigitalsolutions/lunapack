## ADDED Requirements

### Requirement: Publish project lock-file schema

The repository SHALL publish a JSON Schema under `projects/schema/` for
`lunapack-lock.yml`. The schema SHALL require its explicit schema version and a
resolved pack graph with exact pack identity and version, source provenance,
composite references, and managed target-path SHA-256 records. It SHALL reject
unknown lock schema versions and incomplete resolved pack records.

#### Scenario: Validate resolved composite lock state

- **WHEN** the lock schema validates the state produced for a composite pack
  and its transitive packs
- **THEN** validation succeeds

#### Scenario: Reject incomplete resolved state

- **WHEN** the lock schema validates a resolved pack record without source
  provenance, an exact version, or a required managed-file digest
- **THEN** validation fails

## MODIFIED Requirements

### Requirement: Publish project-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for
`lunapack.yml`. The schema SHALL require schema version `1`, define `sources` as
local-source entries with relative paths, and define `packs` as requested root
pack references. Requested root pack references SHALL include an ID and MAY
include an explicit Semantic Version request. The schema SHALL reject absolute
source paths, unsupported source types, resolved source provenance, managed
file ownership, digests, and unknown required-state omissions.

#### Scenario: Validate an initialized manifest

- **WHEN** the schema validates a manifest created by `lunapack init`
- **THEN** validation succeeds

#### Scenario: Reject an unsupported source type

- **WHEN** the schema validates a manifest containing a non-local source type
- **THEN** validation fails

#### Scenario: Reject an absolute local source path

- **WHEN** the schema validates a local source path rooted at a filesystem
  drive, UNC location, or root directory
- **THEN** validation fails

#### Scenario: Reject resolved installation state in configuration

- **WHEN** the schema validates `lunapack.yml` containing a resolved source path,
  managed-file list, or content digest
- **THEN** validation fails

### Requirement: Publish local pack-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for
`pack.yml`. The schema SHALL require a pack identity and semantic version and
allow an optional human-readable package description. A pack SHALL declare one
or more managed-file entries, one or more composite pack references, or both.
Each composite reference SHALL contain a pack ID and an exact Semantic Version.
Pack manifests SHALL not contain source configuration.

#### Scenario: Preserve manifests without a description

- **WHEN** the schema validates an existing complete pack manifest without a
  description
- **THEN** validation succeeds

#### Scenario: Reject an incomplete pack manifest

- **WHEN** the schema validates a pack manifest without a version or managed-file declaration
- **THEN** validation fails

#### Scenario: Validate the dotnet gitignore pack manifest

- **WHEN** the schema validates the repository's `dotnet-gitignore` pack
  manifest
- **THEN** validation succeeds

#### Scenario: Validate a manifest with a description

- **WHEN** the schema validates a pack manifest with a description and a
  managed-file declaration or composite pack reference
- **THEN** validation succeeds

#### Scenario: Preserve file-only manifests

- **WHEN** the schema validates an existing complete pack manifest that
  declares managed files but no composite references
- **THEN** validation succeeds

#### Scenario: Validate a contentless composite manifest

- **WHEN** the schema validates a pack manifest with one or more composite
  references and no managed files
- **THEN** validation succeeds

#### Scenario: Reject an incomplete or unpinned composite reference

- **WHEN** the schema validates a pack manifest without a managed-file or
  composite declaration, or with a composite reference lacking an exact
  version
- **THEN** validation fails

#### Scenario: Reject a source declaration in a pack manifest

- **WHEN** the schema validates a pack manifest containing source configuration
- **THEN** validation fails

### Requirement: Maintain schema compatibility deliberately

The project configuration schema SHALL retain explicit schema version `1`, and
the lock-file schema SHALL use its own explicit schema version. LunaPack SHALL
not support the former version-1 document shape that contains resolved source
provenance or managed-file ownership. Future incompatible lock-file changes
SHALL use a new lock-file schema version.

#### Scenario: Reject an unknown schema version

- **WHEN** either schema validates a document with an unsupported schema version
- **THEN** validation fails

#### Scenario: Reject a former combined-state manifest

- **WHEN** LunaPack reads a version-1 `lunapack.yml` that contains resolved source
  provenance, managed-file ownership, or content digests
- **THEN** it rejects the document as invalid project configuration
