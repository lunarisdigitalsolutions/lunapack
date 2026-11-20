# Spec Delta

## ADDED Requirements

### Requirement: Publish scoped insecure Git preference

The `lunapack.yml` schema SHALL allow optional Boolean
`preferences.git.allowInsecureTransport`. The user-settings schema SHALL allow
the same preference under `global.preferences` and
`projects.<canonical-project-key>.preferences`. Existing version-1 project
configurations without this preference SHALL remain valid.

#### Scenario: Validate project-scoped preference

- **WHEN** a version-1 project configuration sets
  `preferences.git.allowInsecureTransport` to a Boolean
- **THEN** the project schema accepts the configuration

#### Scenario: Validate global and user-project preferences

- **WHEN** user settings set `git.allowInsecureTransport` under global or
  canonical project preferences to a Boolean
- **THEN** the user-settings schema accepts the settings

#### Scenario: Reject a non-Boolean preference

- **WHEN** either schema contains a non-Boolean
  `git.allowInsecureTransport` value
- **THEN** schema validation fails
