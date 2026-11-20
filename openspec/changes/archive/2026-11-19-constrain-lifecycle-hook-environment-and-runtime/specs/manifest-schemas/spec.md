# Spec Delta

## ADDED Requirements

### Requirement: Define lifecycle script policy in project configuration schema

The version-1 `lunapack.yml` JSON Schema SHALL allow optional `trust.scripts` settings with a unique list of non-empty `allowedEnvironmentVariables` and an integer `timeoutSeconds` from 1 through 1,800, inclusive. Existing version-1 documents that omit these settings SHALL remain valid without migration.

#### Scenario: Validate project lifecycle script settings

- **WHEN** a project manifest contains valid allowed-variable names and a timeout from 1 through 1,800 seconds
- **THEN** project-schema validation succeeds

#### Scenario: Validate project policy with omitted settings

- **WHEN** a version-1 project manifest omits `trust.scripts` or either optional setting
- **THEN** project-schema validation succeeds without migration

#### Scenario: Reject an invalid project timeout

- **WHEN** project configuration sets `timeoutSeconds` below 1, above 1,800, or to a non-integer value
- **THEN** project-schema validation fails

#### Scenario: Reject invalid project variable names

- **WHEN** project configuration lists an empty or duplicate variable name, or a name containing `=` or NUL
- **THEN** project-schema validation fails

### Requirement: Define lifecycle script policy in a user sidecar

The user-settings contract SHALL define `<profile>/.lunapack/lifecycle-hook-policy.yml` with optional global-user and canonical local-project policy records containing the same lifecycle script settings. The policy sidecar SHALL remain separate from `config.yml`, and invalid values SHALL be rejected before script execution.

#### Scenario: Validate global and local-project sidecar policy

- **WHEN** the sidecar contains valid global-user and canonical local-project lifecycle script settings
- **THEN** LunaPack accepts both policies in their respective scopes

#### Scenario: Preserve legacy user settings compatibility

- **WHEN** LunaPack saves lifecycle script policy for a user
- **THEN** `config.yml` remains unchanged and readable by older strict CLI readers

#### Scenario: Accept existing user settings without policy

- **WHEN** LunaPack reads an existing `config.yml` without lifecycle script settings or a sidecar that omits a policy scope
- **THEN** it accepts the existing settings without migration and uses normal defaults

#### Scenario: Reject invalid user policy before execution

- **WHEN** global-user or local-project sidecar settings contain an invalid variable name or timeout
- **THEN** LunaPack reports invalid settings and does not execute lifecycle scripts

### Requirement: Keep lifecycle script policy out of pack manifests

The `pack.yml` schema SHALL NOT accept environment allow-lists or script timeouts as pack-authored lifecycle policy. Those settings remain project or user policy.

#### Scenario: Reject pack-authored lifecycle policy

- **WHEN** a pack manifest declares an environment allow-list or timeout as lifecycle policy
- **THEN** pack-schema validation fails
