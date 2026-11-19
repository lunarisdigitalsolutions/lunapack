# Spec Delta

## ADDED Requirements

### Requirement: Screen managed source content before publication

`luna publish` SHALL screen eligible source payloads selected by the pack's managed-file declarations before uploading any release content. Findings SHALL use source-relative paths and follow the warning, enforcement, and override behavior defined by `pack-content-screening`.

#### Scenario: Report a source finding during warning-only publication

- **WHEN** a managed source file contains a hidden character during warning-only rollout
- **THEN** LunaPack reports the finding and publication continues under existing validation rules

#### Scenario: Reject publication before upload

- **WHEN** enforcement is enabled and a managed source contains a high-confidence finding without an override
- **THEN** LunaPack returns a non-success result before uploading the release

#### Scenario: Override a high-confidence publication finding

- **WHEN** enforcement is enabled and an author runs `luna publish` with `--allow-hidden-characters`
- **THEN** LunaPack reports findings and continues publication under existing validation rules
