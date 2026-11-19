# Spec Delta

## Purpose

Detect hidden Unicode characters in pack-managed text and provide consistent, actionable findings before lifecycle mutation, validation success, or publication.

## ADDED Requirements

### Requirement: Screen eligible text payloads

LunaPack SHALL screen valid UTF-8 text payloads. It SHALL skip binary payloads and payloads that cannot be decoded as UTF-8 without attempting a replacement-character decode.

#### Scenario: Screen valid UTF-8 text

- **WHEN** an operation supplies a valid UTF-8 text payload for screening
- **THEN** LunaPack scans the payload for hidden Unicode characters

#### Scenario: Skip binary or invalid UTF-8 payloads

- **WHEN** an operation supplies a binary payload or bytes that cannot be decoded as UTF-8
- **THEN** LunaPack skips screening that payload without failing solely because it was skipped

### Requirement: Report precise finding locations

Every finding SHALL report its operation-appropriate relative path, 1-based line, 1-based Unicode-scalar column, code point in `U+` hexadecimal notation, and severity. Paths SHALL use `/`; columns SHALL count Unicode scalar values rather than UTF-16 code units.

#### Scenario: Report an authored source location

- **WHEN** screening finds a character in `templates/readme.md` during author validation
- **THEN** the finding identifies `templates/readme.md` and the character's line, scalar column, code point, and severity

#### Scenario: Report a remapped planned target

- **WHEN** screening finds a character in a planned file whose effective target is `docs/readme.md`
- **THEN** the finding reports `docs/readme.md` rather than the manifest-declared target

#### Scenario: Count columns by Unicode scalar

- **WHEN** a hidden character follows a supplementary Unicode scalar on the same line
- **THEN** its reported column counts the supplementary scalar once

### Requirement: Classify hidden format characters conservatively

LunaPack SHALL assign high-confidence severity to Unicode `Bidi_Control` characters and U+200B ZERO WIDTH SPACE. It SHALL report other Unicode `Format` characters as warnings and SHALL preserve valid emoji ZWJ and variation-selector sequences without findings.

#### Scenario: Classify a bidi control as high confidence

- **WHEN** screening finds a Unicode `Bidi_Control` character
- **THEN** the finding has high-confidence severity

#### Scenario: Classify a different format character as a warning

- **WHEN** screening finds a Unicode `Format` character outside the high-confidence set
- **THEN** the finding has warning severity

#### Scenario: Preserve a valid emoji ZWJ sequence

- **WHEN** a zero-width joiner participates in a valid emoji ZWJ sequence
- **THEN** screening does not report the joiner as a finding

#### Scenario: Preserve a valid emoji variation sequence

- **WHEN** a variation selector participates in a valid emoji variation sequence
- **THEN** screening does not report the selector as a finding

### Requirement: Keep the initial rollout warning-only

During the initial warning-only rollout, findings SHALL not by themselves change an operation's exit status or prevent its planned writes or upload.

#### Scenario: Continue an install with findings during warning-only rollout

- **WHEN** an install plan contains high-confidence findings during warning-only rollout
- **THEN** LunaPack reports them and continues the install under existing lifecycle rules

#### Scenario: Continue publication with findings during warning-only rollout

- **WHEN** a pack contains findings during warning-only rollout
- **THEN** LunaPack reports them and publication continues under existing validation rules

### Requirement: Enforce high-confidence findings with an explicit override

After false-positive review enables enforcement, install, update, pack validation, and publication SHALL fail before their side effects when high-confidence findings exist unless the user supplies `--allow-hidden-characters`. The override SHALL not suppress findings, and warning-severity findings SHALL not block these operations.

#### Scenario: Block installation before mutation

- **WHEN** enforcement is enabled, an install plan contains a high-confidence finding, and no override is supplied
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

#### Scenario: Block publication before upload

- **WHEN** enforcement is enabled, a pack contains a high-confidence finding, and no override is supplied
- **THEN** LunaPack returns a non-success result without uploading the pack

#### Scenario: Continue with an explicit override

- **WHEN** enforcement is enabled and a user supplies `--allow-hidden-characters`
- **THEN** LunaPack reports all findings and continues the operation under its existing validation and transaction rules
