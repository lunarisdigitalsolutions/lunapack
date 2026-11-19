# Spec Delta

## ADDED Requirements

### Requirement: Verify locked managed files in strict audit

`luna audit --strict` SHALL compare each pack- and link-managed target recorded
in `lunapack-lock.yml` with its SHA-256 digest. It SHALL identify every missing
or mismatched target by owner and path without mutating project files or state.
By default it SHALL scan all targets; with `--fail-fast`, it SHALL stop after
the first missing or mismatched target. Other audit statuses SHALL remain
informational.

#### Scenario: Aggregate pack and link violations

- **WHEN** strict audit finds missing or mismatched targets in multiple pack or
  link records
- **THEN** it reports each affected owner and path and continues scanning the
  remaining targets

#### Scenario: Stop after the first integrity violation

- **WHEN** strict audit runs with `--fail-fast` and finds a missing or
  mismatched target
- **THEN** it reports that violation and stops scanning subsequent targets

#### Scenario: Preserve the default report-only audit

- **WHEN** a user runs `luna audit` without `--strict`
- **THEN** LunaPack preserves its existing report-only behavior and exit status

#### Scenario: Require strict mode for fail-fast

- **WHEN** a user runs `luna audit --fail-fast` without `--strict`
- **THEN** LunaPack reports invalid usage and returns exit code `1`

### Requirement: Confine strict audit reads

Strict audit SHALL normalize every lock target beneath the selected workspace
and SHALL reject existing symbolic-link or reparse-point ancestors and targets
as operation errors. It SHALL NOT hash content reached outside the workspace.

#### Scenario: Reject a target outside the workspace

- **WHEN** strict audit encounters a drive-relative or otherwise escaping
  target, or a target that crosses an existing filesystem alias
- **THEN** it reports an operation error without hashing outside content and
  returns exit code `1`

### Requirement: Distinguish strict audit outcomes

`luna audit --strict` SHALL return exit code `0` when all locked targets match,
`2` when one or more targets are missing or modified, and `1` for invalid usage
or unreadable project or target state. It SHALL report unreadable state as an
operation error, not as an integrity violation.

#### Scenario: Return success for matching targets

- **WHEN** strict audit can read project state and every locked target matches
  its recorded digest
- **THEN** it returns exit code `0`

#### Scenario: Return the integrity-violation code

- **WHEN** strict audit completes with one or more missing or modified targets
- **THEN** it returns exit code `2`

#### Scenario: Return the operation-error code

- **WHEN** strict audit encounters invalid command usage or cannot read project
  or target state, including explicit-null required collections
- **THEN** it reports the operation error and returns exit code `1`
