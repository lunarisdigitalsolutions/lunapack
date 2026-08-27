# Manifest Schemas Delta Specification

## ADDED Requirements

### Requirement: Define Luna Link configuration

The version-1 `lunapack.yml` JSON Schema SHALL allow an optional `links` mapping keyed by a LunaPack pack-ID-shaped name. Each link SHALL require a configured source name and a non-empty unique `includes` array. It SHALL allow unique `excludes`, an optional project-relative base `path`, an optional project-relative `target`, an optional Git `ref`, an optional project-relative `stripPrefix`, and an optional boolean `flatten`. The schema SHALL reject unknown properties, rooted or syntactically escaping paths, empty selectors, and invalid names. Existing valid version-1 configurations that omit links SHALL remain valid.

#### Scenario: Validate a complete link definition

- **WHEN** the schema validates a link with source, includes, excludes, base path, target, Git ref, strip prefix, and flattening
- **THEN** validation succeeds when every value has the required type and safe syntax

#### Scenario: Validate existing configuration without links

- **WHEN** the schema validates an existing version-1 project configuration that omits `links`
- **THEN** validation succeeds without a schema-version migration

#### Scenario: Reject an unsafe link path

- **WHEN** a link base path, target, or strip prefix is rooted or contains a parent traversal segment
- **THEN** project-configuration schema validation fails

### Requirement: Define resolved Luna Link state

The current `lunapack-lock.yml` JSON Schema SHALL allow a resolved `links` mapping keyed by link name. Each record SHALL require configured-source identity, a canonical SHA-256 digest of the installed definition, and a non-empty selected-file collection. Git-backed records SHALL require the effective ref and immutable resolved commit. Each selected-file record SHALL require its normalized source path, declared target identity, effective project-relative target path, and installed SHA-256 content digest. Existing valid lock files that omit links SHALL remain valid.

#### Scenario: Validate resolved Git link state

- **WHEN** the lock schema validates a Git link record with source identity, definition digest, effective ref, resolved commit, and complete selected-file records
- **THEN** validation succeeds

#### Scenario: Validate resolved local link state

- **WHEN** the lock schema validates a local link record with source identity, definition digest, and complete selected-file records but no Git commit
- **THEN** validation succeeds

#### Scenario: Reject incomplete selected-file ownership

- **WHEN** a resolved link file omits its source path, declared target, effective target, or installed digest
- **THEN** lock-file schema validation fails
