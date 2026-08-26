## MODIFIED Requirements

### Requirement: Publish local pack-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for
`pack.yml`. The schema SHALL require a pack identity and semantic version and
SHALL allow identity-only manifests with empty managed-file and composite-pack
collections for incremental authoring. It SHALL allow optional non-empty name,
author, homepage, and license metadata, an optional human-readable package
description, and up to 15 unique, non-empty tags. A complete distributable pack
MAY declare managed-file entries, composite pack references, or both. Each
composite reference SHALL contain a pack ID and an exact Semantic Version and
MAY bind identifier-named string or boolean parameters for its referenced pack.
Managed-file selectors MAY set `template` to opt into Scriban parsing; it
defaults to false. Pack manifests SHALL not contain source configuration.
Existing valid pack manifests SHALL remain valid.

#### Scenario: Validate an identity-only manifest

- **WHEN** the schema validates a manifest containing an ID, semantic version,
  and empty content collections
- **THEN** validation succeeds

#### Scenario: Validate optional metadata

- **WHEN** the schema validates a manifest with non-empty name, author, homepage,
  and license values
- **THEN** validation succeeds

#### Scenario: Reject invalid optional metadata

- **WHEN** optional name, author, homepage, or license metadata is present but
  empty or the homepage is not a supported absolute URI
- **THEN** validation fails

#### Scenario: Preserve manifests without a description

- **WHEN** the schema validates an existing complete pack manifest without a
  description
- **THEN** validation succeeds

#### Scenario: Reject an incomplete pack identity

- **WHEN** the schema validates a pack manifest without an ID or version
- **THEN** validation fails

#### Scenario: Validate the dotnet gitignore pack manifest

- **WHEN** the schema validates the repository's `dotnet-gitignore` pack
  manifest
- **THEN** validation succeeds

#### Scenario: Validate a manifest with a description

- **WHEN** the schema validates a pack manifest with a description and a
  managed-file declaration or composite pack reference
- **THEN** validation succeeds

#### Scenario: Validate bounded pack tags

- **WHEN** the schema validates a pack manifest with up to 15 unique, non-empty
  tags
- **THEN** validation succeeds

#### Scenario: Reject excessive pack tags

- **WHEN** the schema validates a pack manifest with more than 15 tags
- **THEN** validation fails

#### Scenario: Preserve file-only manifests

- **WHEN** the schema validates an existing complete pack manifest that declares
  managed files but no composite references
- **THEN** validation succeeds

#### Scenario: Validate a contentless composite manifest

- **WHEN** the schema validates a pack manifest with one or more composite
  references and no managed files
- **THEN** validation succeeds

#### Scenario: Reject an incomplete or unpinned composite reference

- **WHEN** the schema validates a composite reference lacking a pack ID or exact
  version
- **THEN** validation fails

#### Scenario: Validate composite reference parameter bindings

- **WHEN** a composite reference binds identifier-named string or boolean
  parameters
- **THEN** the pack manifest is valid

#### Scenario: Reject a source declaration in a pack manifest

- **WHEN** the schema validates a pack manifest containing source configuration
- **THEN** validation fails

#### Scenario: Preserve a managed file without template parsing

- **WHEN** a managed-file selector omits `template`
- **THEN** the manifest is valid and the selector defaults to non-template
  handling
