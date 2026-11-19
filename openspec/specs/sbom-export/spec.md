# SBOM Export Specification

## Purpose

Lets project consumers export portable software inventories that describe the exact pack and managed-file evidence already recorded in LunaPack lock state.

## Requirements

### Requirement: Select a versioned JSON target

`luna sbom` SHALL require one target: `cyclonedx-1.6`, `cyclonedx-1.7`, `spdx-2.3`, or `spdx-3.0.1`. The selected target SHALL determine the document standard and specification version. Unsupported targets SHALL fail with a non-zero result.

#### Scenario: Export to each supported target

- **WHEN** a consumer selects any supported target
- **THEN** Luna emits a JSON document conforming to that target's official schema and declaring that target's specification version

#### Scenario: Reject an unsupported target

- **WHEN** a consumer selects a target not in the supported set
- **THEN** Luna reports the invalid target and returns a non-zero result without writing an output document

### Requirement: Export only validated lock evidence

The exporter SHALL derive its inventory from validated project lock state without contacting catalogs, Git sources, OCI registries, or other networks. It SHALL not read or rehash current managed-file content and SHALL not modify project configuration, lock state, or managed files.

#### Scenario: Export while sources are unreachable

- **WHEN** a project has valid lock state but its configured remote sources are unavailable
- **THEN** Luna exports the locked inventory without attempting remote access

#### Scenario: Preserve the workspace during export

- **WHEN** a consumer exports an inventory
- **THEN** Luna leaves project configuration, lock state, and managed-file bytes unchanged

### Requirement: Include locked packs, links, and managed-file evidence

Each document SHALL represent every resolved pack and its exact locked dependency edges, every resolved Luna Link, and each pack- or link-managed file with its lock-recorded SHA-256 digest. It SHALL include recorded source identities and Git or OCI provenance, including resolved refs, commits, registry coordinates, and digests when present. It SHALL omit unavailable optional evidence rather than inventing values.

#### Scenario: Export a composite pack graph

- **WHEN** lock state contains a root pack and its resolved transitive dependencies
- **THEN** the document contains each resolved pack and relationships matching the lock's direct dependency edges

#### Scenario: Export a managed file

- **WHEN** lock state contains a pack- or link-managed file
- **THEN** the document contains that file's effective target and recorded SHA-256 digest associated with its owning pack or link

#### Scenario: Export mixed source provenance

- **WHEN** lock state contains local, Git, and OCI pack sources and Git-backed links
- **THEN** the document includes each recorded source identity and applicable immutable Git or OCI evidence without querying the sources

### Requirement: Use stable identifiers and ordering

Component, relationship, and managed-file identifiers SHALL be derived from stable lock identities, versions, owners, and effective targets. Exported collections and relationships SHALL use deterministic ordering. SPDX document-generation identifiers MAY include schema-required current UTC creation timestamps, but package, link, file, and relationship inventory identifiers SHALL remain stable for unchanged lock state.

#### Scenario: Repeat an export for unchanged lock state

- **WHEN** a consumer exports the same lock state and target more than once
- **THEN** inventory element identifiers, content, and collection ordering remain identical; only SPDX document-generation metadata and identifiers vary with schema-required UTC creation time

#### Scenario: Disambiguate managed files

- **WHEN** two locked roots own distinct managed-file records
- **THEN** each file has a stable identifier derived from its owner and effective target

#### Scenario: Reject ambiguous managed-file identities

- **WHEN** one pack or Luna Link records the same effective target more than once
- **THEN** lock validation fails and Luna emits no dependency inventory

### Requirement: Write to standard output or a selected file

The exporter SHALL write raw JSON to standard output when `--output` is omitted. When `--output <path>` is supplied, Luna SHALL write the JSON document to that project-relative workspace path, accepting either input separator and rejecting rooted or escaping paths. Successful standard-output export SHALL contain no human-readable status text.

#### Scenario: Write JSON to standard output

- **WHEN** a consumer selects a valid target without `--output`
- **THEN** standard output contains only the generated JSON document

#### Scenario: Write JSON to a file

- **WHEN** a consumer supplies a valid `--output` path
- **THEN** Luna writes the selected JSON document to that workspace-relative path and returns success

#### Scenario: Reject an escaping output path

- **WHEN** a consumer supplies a rooted or workspace-escaping output path
- **THEN** Luna reports the invalid path, returns a non-zero result, and writes nothing outside the workspace

#### Scenario: Reject output that would overwrite project state

- **WHEN** a consumer selects `lunapack.yml`, `lunapack-lock.yml`, or a locked managed-file target as the output path
- **THEN** Luna reports the conflict, returns a non-zero result, and leaves the existing file unchanged

#### Scenario: Report an output failure

- **WHEN** Luna cannot create or write the requested output file
- **THEN** Luna reports the filesystem failure and returns a non-zero result

### Requirement: Disclaim signature and attestation status

Each generated document SHALL state, in a schema-valid field when the selected format permits, that it is an inventory of lock-file evidence and is not a signature or attestation. CLI help and developer documentation SHALL state the same limitation.

#### Scenario: Identify a generated inventory

- **WHEN** Luna emits a target with a schema-supported disclaimer field
- **THEN** the document states that the inventory is not a signature or attestation

#### Scenario: Document the limitation for every target

- **WHEN** a consumer reads `luna sbom` help or its developer reference
- **THEN** the documentation states that exported inventories are not signatures or attestations
