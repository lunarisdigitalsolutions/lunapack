# Spec Delta

## ADDED Requirements

### Requirement: Define direct-installability metadata

The `pack.yml` schema SHALL allow an optional Boolean `directInstallable`
property. When omitted, LunaPack SHALL treat the pack as directly installable.
Existing valid manifests that omit the property SHALL remain valid.

#### Scenario: Validate an explicitly installable pack

- **WHEN** a pack manifest declares `directInstallable: true`
- **THEN** the manifest is valid and the pack is directly installable

#### Scenario: Default an omitted installability property

- **WHEN** a valid pack manifest omits `directInstallable`
- **THEN** the pack remains directly installable

#### Scenario: Validate a dependency-only pack

- **WHEN** a pack manifest declares `directInstallable: false`
- **THEN** the manifest is valid and the pack is reserved for dependency use

#### Scenario: Validate a dependency-only local manifest

- **WHEN** an author runs `luna pack validate` for an otherwise valid manifest
  with `directInstallable: false`
- **THEN** validation succeeds

#### Scenario: Reject a non-Boolean installability value

- **WHEN** a pack manifest declares a non-Boolean `directInstallable` value
- **THEN** schema validation fails

### Requirement: Define project-local CLI preferences

The version-1 `lunapack.yml` schema SHALL allow an optional `preferences`
object with optional `discover` and `search` objects containing Boolean
`onlyComposites` values, and an optional `nextSteps` object containing a
Boolean `enabled` value. These objects SHALL reject unknown properties.
Existing valid project configurations that omit preferences SHALL remain valid.

#### Scenario: Validate project-local preferences

- **WHEN** `lunapack.yml` declares one or more supported Boolean preferences
- **THEN** project-configuration schema validation succeeds

#### Scenario: Preserve a project without preferences

- **WHEN** the schema validates an existing version-1 configuration that omits
  `preferences`
- **THEN** validation succeeds without a schema-version migration

#### Scenario: Reject an invalid project preference

- **WHEN** `lunapack.yml` declares an unsupported preference or a non-Boolean
  preference value
- **THEN** project-configuration schema validation fails

### Requirement: Define user-scoped CLI preferences

The `~/.lunapack/config.yml` user-settings document SHALL allow the supported
Boolean preferences in user-global settings and in project records keyed by a
canonical absolute project directory. Existing user settings without
preferences SHALL remain valid, and preference updates SHALL preserve existing
trust data.

#### Scenario: Validate global and project-specific preferences

- **WHEN** user settings declare supported preferences globally and under a
  canonical project-directory key
- **THEN** user-settings validation succeeds

#### Scenario: Preserve existing user trust settings

- **WHEN** LunaPack writes a preference while user settings contain trust
  grants or denial policies
- **THEN** the trust data remains unchanged

#### Scenario: Reject an invalid user preference

- **WHEN** user settings contain an unsupported preference, a non-Boolean
  value, or a non-canonical project key
- **THEN** user-settings validation fails
