## ADDED Requirements

### Requirement: Represent optional pack destinations in version-1 state

The project-configuration and lock-file schemas SHALL allow an optional,
non-empty, project-relative `destination` for directly requested packs. The
lock-file schema SHALL allow the corresponding resolved destination while
retaining every effective managed target path and digest. Existing valid
version-1 state files that omit destination metadata SHALL remain valid.

#### Scenario: Validate destination-installed pack state

- **WHEN** the schemas validate state written after a destination-installed
  pack succeeds
- **THEN** the project configuration and lock file both validate and retain the
  requested destination

#### Scenario: Validate existing state without a destination

- **WHEN** the schemas validate a pre-destination version-1 configuration and
  lock file
- **THEN** validation succeeds without a schema-version migration

#### Scenario: Reject an unsafe persisted destination

- **WHEN** either schema validates an absolute destination or one that escapes
  the project root
- **THEN** validation fails
