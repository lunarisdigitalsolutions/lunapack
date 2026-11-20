# Spec Delta

## ADDED Requirements

### Requirement: Preserve modified obsolete link targets

When a link update removes a previously selected target, LunaPack SHALL compare
the current target digest with the locked digest. It SHALL delete an unchanged
target, preserve a modified target with a warning, and omit stale ownership from
the successful next lock state. A missing target SHALL lose stale ownership
without a deletion action. The pack-only `--remove-modified` option SHALL NOT
authorize link-target deletion.

#### Scenario: Preserve a modified link target removed from selection

- **WHEN** a link update no longer selects a target whose current digest differs
  from its locked digest
- **THEN** LunaPack preserves the target, warns after state is persisted, and
  removes its stale link ownership

#### Scenario: Delete an unchanged obsolete link target

- **WHEN** a link update no longer selects a target whose current digest matches
  its locked digest
- **THEN** LunaPack deletes the target and removes its lock record

#### Scenario: Remove stale ownership for an absent link target

- **WHEN** a link update no longer selects a target that is already absent
- **THEN** LunaPack completes the update without creating a deletion action

#### Scenario: Reject pack-only modified removal for a link

- **WHEN** a user supplies `--remove-modified` while updating a named link
- **THEN** LunaPack fails without changing the link or its targets

#### Scenario: Reject pack-only dry run for a link

- **WHEN** a user supplies `--dry-run` while updating a named link
- **THEN** LunaPack fails without changing the link or its targets
