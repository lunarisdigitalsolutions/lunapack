# Spec Delta

## ADDED Requirements

### Requirement: Honor the next-step guidance preference

LunaPack SHALL use the effective `next-steps.enabled` preference to control
workflow recommendations. When the preference is absent, guidance SHALL remain
enabled. When disabled, LunaPack SHALL omit recommendations without changing
the primary command's output, state changes, or exit status. The
`--suppress-next-steps` option SHALL continue to suppress guidance for one
invocation even when the preference is enabled.

#### Scenario: Show guidance when the preference is unset

- **WHEN** a command normally emits workflow recommendations and no scope
  configures `next-steps.enabled`
- **THEN** LunaPack emits recommendations as before

#### Scenario: Suppress guidance through configuration

- **WHEN** the effective `next-steps.enabled` preference is `false`
- **THEN** LunaPack omits workflow recommendations while preserving the
  primary command's result

#### Scenario: Suppress guidance for one invocation

- **WHEN** the effective preference is `true` and a user supplies
  `--suppress-next-steps`
- **THEN** LunaPack omits workflow recommendations for that invocation
