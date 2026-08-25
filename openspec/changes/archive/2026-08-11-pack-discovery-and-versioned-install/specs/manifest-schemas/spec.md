# Manifest Schemas Delta

## MODIFIED Requirements

### Requirement: Publish local pack-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for
`pack.yml`. The schema SHALL require a pack identity, semantic version, and a
non-empty managed-file declaration with source and target paths. The schema
SHALL allow an optional human-readable package description.

#### Scenario: Validate the dotnet gitignore pack manifest

- **WHEN** the schema validates the repository's `dotnet-gitignore` pack
  manifest
- **THEN** validation succeeds

#### Scenario: Validate a manifest with a description

- **WHEN** the schema validates a pack manifest with a description and all
  required pack fields
- **THEN** validation succeeds

#### Scenario: Preserve manifests without a description

- **WHEN** the schema validates an existing complete pack manifest without a
  description
- **THEN** validation succeeds

#### Scenario: Reject an incomplete pack manifest

- **WHEN** the schema validates a pack manifest without a version or
  managed-file declaration
- **THEN** validation fails
