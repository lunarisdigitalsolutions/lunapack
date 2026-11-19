# Spec Delta

## ADDED Requirements

### Requirement: Screen authored managed content during validation

`luna pack validate` SHALL screen valid UTF-8 source payloads selected by the pack's managed-file declarations, including resolved external-source selections, and report findings using source-relative paths.

#### Scenario: Report a source finding during warning-only validation

- **WHEN** a managed source file contains a hidden character during warning-only rollout
- **THEN** `luna pack validate` reports the finding and succeeds if no other validation error exists

#### Scenario: Reject a high-confidence source finding

- **WHEN** enforcement is enabled and a managed source contains a high-confidence finding without an override
- **THEN** `luna pack validate` reports the finding and exits unsuccessfully

#### Scenario: Override a high-confidence validation finding

- **WHEN** enforcement is enabled and an author runs `luna pack validate --allow-hidden-characters`
- **THEN** LunaPack reports findings and continues validation without treating them as blockers
