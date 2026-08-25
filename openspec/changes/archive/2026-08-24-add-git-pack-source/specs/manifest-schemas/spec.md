## MODIFIED Requirements

### Requirement: Publish project-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `lunapack.yml`. The schema SHALL require schema version `1`, define local and Git source entries, and define requested root pack references. A Git source SHALL require a repository URL and SHALL allow optional `ref`, optional repository-relative `path`, and optional `timeoutSeconds` from 1 through 300. Requested root pack references SHALL include an ID and MAY include an explicit Semantic Version request. The schema SHALL reject absolute local source paths, unsafe Git source paths, unsupported source types, resolved source provenance, managed file ownership, digests, and unknown required-state omissions. Existing valid local-source configuration SHALL remain valid.

#### Scenario: Validate an initialized manifest

- **WHEN** the schema validates a manifest created by `luna init`
- **THEN** validation succeeds

#### Scenario: Validate a Git source

- **WHEN** the schema validates a Git source with a repository URL and optional valid ref, path, and timeout
- **THEN** validation succeeds

#### Scenario: Reject an unsupported source type

- **WHEN** the schema validates a manifest containing a source type other than local or Git
- **THEN** validation fails

#### Scenario: Reject an absolute local source path

- **WHEN** the schema validates a local source path rooted at a filesystem drive, UNC location, or root directory
- **THEN** validation fails

#### Scenario: Reject an unsafe Git source path

- **WHEN** the schema validates a Git source path that is absolute or escapes the repository root
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
