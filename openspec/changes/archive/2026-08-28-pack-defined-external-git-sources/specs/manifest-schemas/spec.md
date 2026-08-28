# Manifest Schemas Delta Specification

## MODIFIED Requirements

### Requirement: Publish project lock-file schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `lunapack-lock.yml`. The schema SHALL require its explicit schema version and a resolved pack graph with exact pack identity and version, source provenance, composite references, and managed target-path SHA-256 records. Git-sourced pack provenance SHALL record the repository URL, requested ref when configured, configured repository path when configured, and the resolved commit SHA. A pack that uses an external source SHALL record each used pack-local source alias, its authoritative workspace source identifier, normalized fingerprint, canonical requested ref, and resolved commit. Each externally sourced managed-file record SHALL identify its owning pack, pack version, pack-local source alias, workspace source identifier, fingerprint, source-relative path, effective target, and installed content hash. The schema SHALL reject unknown lock schema versions and incomplete resolved pack or external-source records. Existing valid local-source and Git-source lock records that contain no external-source provenance SHALL remain valid.

#### Scenario: Validate resolved composite lock state

- **WHEN** the lock schema validates the state produced for a composite pack and its transitive packs
- **THEN** validation succeeds

#### Scenario: Validate Git-resolved lock state

- **WHEN** the lock schema validates a Git-sourced pack record with its repository URL and resolved commit SHA
- **THEN** validation succeeds

#### Scenario: Validate external-source provenance

- **WHEN** the lock schema validates an externally sourced file and its pack alias mapping with all required identity, revision, path, ownership, and hash fields
- **THEN** validation succeeds

#### Scenario: Reject incomplete resolved state

- **WHEN** the lock schema validates a resolved pack record without source provenance, an exact version, or a required managed-file digest
- **THEN** validation fails

#### Scenario: Reject Git provenance without a resolved commit

- **WHEN** the lock schema validates a Git-sourced pack or external-source record without a resolved commit SHA
- **THEN** validation fails

### Requirement: Publish local pack-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `pack.yml`. The schema SHALL require a pack identity, semantic version, non-empty author, and non-empty license. It SHALL allow empty managed-file and composite-pack collections for incremental authoring. It SHALL allow optional non-empty name and homepage metadata, an optional human-readable package description, and up to 15 unique, non-empty tags. A complete distributable pack MAY declare managed-file entries, composite pack references, or both. Each composite reference SHALL contain a pack ID and an exact Semantic Version and MAY bind identifier-named string or boolean parameters for its referenced pack. Managed-file selectors MAY set `template` to opt into Scriban parsing; it defaults to false.

The schema SHALL allow an optional `sources` mapping whose keys are pack-local aliases and whose values are Git source declarations. Each declaration SHALL require `type: git`, a credential-free repository URL, and an explicit ref, and MAY contain a safe repository-relative base `path` and non-empty `description`. Pack-defined local sources and credential placeholders SHALL be invalid. Managed-file selectors MAY name a declared source alias and MAY select a file, recursive directory, or glob with repeatable exclusion patterns and optional flattening. Source and exclusion paths SHALL be relative and SHALL not escape the resolved source root. A selector without a source alias SHALL continue to resolve from the pack source. Lifecycle scripts SHALL resolve only from the pack source and SHALL not reference an external source.

#### Scenario: Reject a manifest without required attribution

- **WHEN** the schema validates a manifest without author or license metadata
- **THEN** validation fails

#### Scenario: Validate optional metadata

- **WHEN** the schema validates a manifest with non-empty name, author, homepage, and license values
- **THEN** validation succeeds

#### Scenario: Reject a pack manifest without attribution

- **WHEN** a pack manifest includes an empty author or license value
- **THEN** validation fails because attribution must be non-empty

#### Scenario: Reject invalid optional metadata

- **WHEN** optional name or homepage metadata is empty or the homepage is not a supported absolute URI
- **THEN** validation fails

#### Scenario: Preserve manifests without a description

- **WHEN** the schema validates an existing complete pack manifest without a description
- **THEN** validation succeeds

#### Scenario: Reject an incomplete pack identity

- **WHEN** the schema validates a pack manifest without an ID or version
- **THEN** validation fails

#### Scenario: Reject an incomplete pack manifest

- **WHEN** the schema validates a pack manifest without a complete identity or with an incomplete managed-file declaration
- **THEN** validation fails

#### Scenario: Validate the dotnet gitignore pack manifest

- **WHEN** the schema validates the repository's `dotnet-gitignore` pack manifest
- **THEN** validation succeeds

#### Scenario: Validate a manifest with a description

- **WHEN** the schema validates a pack manifest with a description and a managed-file declaration or composite pack reference
- **THEN** validation succeeds

#### Scenario: Validate bounded pack tags

- **WHEN** the schema validates a pack manifest with up to 15 unique, non-empty tags
- **THEN** validation succeeds

#### Scenario: Reject excessive pack tags

- **WHEN** the schema validates a pack manifest with more than 15 tags
- **THEN** validation fails

#### Scenario: Preserve file-only manifests

- **WHEN** the schema validates an existing complete pack manifest that declares managed files but no composite references or external sources
- **THEN** validation succeeds

#### Scenario: Validate a contentless composite manifest

- **WHEN** the schema validates a pack manifest with one or more composite references and no managed files
- **THEN** validation succeeds

#### Scenario: Reject an incomplete or unpinned composite reference

- **WHEN** the schema validates a pack manifest without a managed-file or composite declaration, or with a composite reference lacking an exact version
- **THEN** validation fails

#### Scenario: Validate composite reference parameter bindings

- **WHEN** a composite reference binds identifier-named string or boolean parameters
- **THEN** the pack manifest is valid

#### Scenario: Reject a source declaration in a pack manifest

- **WHEN** a pack manifest declares a source that is local, lacks an explicit ref, contains credentials, or otherwise violates the pack-defined Git source contract
- **THEN** validation fails

#### Scenario: Validate a pack-defined Git source

- **WHEN** a pack manifest declares a credential-free Git source with an explicit ref and a managed file references its alias
- **THEN** validation succeeds

#### Scenario: Reject a pack-defined local source

- **WHEN** a pack manifest declares a source with `type: local`
- **THEN** validation fails

#### Scenario: Reject an unpinned pack-defined source

- **WHEN** a pack manifest declares a Git source without a ref
- **THEN** validation fails

#### Scenario: Reject an unknown source alias

- **WHEN** a managed-file selector names an alias absent from the pack's `sources` mapping
- **THEN** validation fails

#### Scenario: Reject an external lifecycle script

- **WHEN** a lifecycle script attempts to select its file from a pack-defined external source
- **THEN** validation fails

#### Scenario: Preserve a managed file without template parsing

- **WHEN** a managed-file selector omits `template`
- **THEN** the manifest is valid and the selector defaults to non-template handling
