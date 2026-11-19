# pack-content-screening Specification

## Purpose

Detect hidden Unicode characters in pack-managed text and provide consistent, actionable findings before lifecycle mutation, validation success, or publication.

## Requirements

### Requirement: Screen eligible text payloads

LunaPack SHALL stream strict UTF-8 decoding and SHALL decode BOM-tagged UTF-16LE, UTF-16BE, UTF-32LE, and UTF-32BE payloads. Opaque NUL-containing or invalid UTF-8 payloads SHALL not fail solely for encoding, but LunaPack SHALL resynchronize after invalid bytes and SHALL enforce any later recoverable hidden controls.

#### Scenario: Screen valid UTF-8 text

- **WHEN** an operation supplies a valid UTF-8 text payload for screening
- **THEN** LunaPack scans the payload for hidden Unicode characters

#### Scenario: Screen BOM-tagged Unicode

- **WHEN** an operation supplies BOM-tagged UTF-16 or UTF-32 text containing a hidden character
- **THEN** LunaPack decodes the payload and reports the hidden character

#### Scenario: Skip an opaque payload without hidden controls

- **WHEN** an operation supplies a NUL-containing or invalid Unicode payload with no recoverable hidden controls
- **THEN** LunaPack does not fail solely because the payload is opaque

#### Scenario: Recover a control from an opaque payload

- **WHEN** a NUL-containing or invalid UTF-8 payload contains a later recoverable U+202E
- **THEN** LunaPack reports and applies high-confidence policy to that character

### Requirement: Report precise finding locations

Every finding SHALL report its operation-appropriate relative path, 1-based line, 1-based Unicode-scalar column, code point in `U+` hexadecimal notation, and severity. Paths SHALL use `/`; columns SHALL count Unicode scalar values rather than UTF-16 code units. During invalid UTF-8 recovery, each invalid byte SHALL occupy one scan position.

#### Scenario: Report an authored source location

- **WHEN** screening finds a character in `templates/readme.md` during author validation
- **THEN** the finding identifies `templates/readme.md` and the character's line, scalar column, code point, and severity

#### Scenario: Report a remapped planned target

- **WHEN** screening finds a character in a planned file whose effective target is `docs/readme.md`
- **THEN** the finding reports `docs/readme.md` rather than the manifest-declared target

#### Scenario: Count columns by Unicode scalar

- **WHEN** a hidden character follows a supplementary Unicode scalar on the same line
- **THEN** its reported column counts the supplementary scalar once

### Requirement: Bound diagnostic retention

LunaPack SHALL retain at most 1,000 findings per payload, SHALL report the number of omitted findings, and SHALL retain independent high-confidence evidence so an omitted blocker cannot bypass policy.

#### Scenario: Block on a finding after the retained limit

- **WHEN** a payload contains 1,000 warning findings followed by a high-confidence finding
- **THEN** LunaPack reports the omission and applies blocking policy to the high-confidence finding

### Requirement: Prevent unsafe managed paths in diagnostics

Managed source and target paths SHALL reject terminal controls, Unicode `Format` characters, line separators, and paragraph separators. Diagnostic rendering SHALL visibly escape unsafe legacy path characters.

#### Scenario: Reject a terminal-control path

- **WHEN** a managed path contains an escape character
- **THEN** path validation fails before planning or mutation

#### Scenario: Escape a legacy diagnostic path

- **WHEN** a persisted path contains a bidi control
- **THEN** diagnostic output renders its code point visibly instead of changing terminal direction

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

#### Scenario: Report an orphan variation selector

- **WHEN** a variation selector does not follow a valid emoji or keycap base
- **THEN** LunaPack reports the selector as a warning

### Requirement: Keep the initial rollout warning-only

During the initial warning-only rollout, findings SHALL not by themselves change an operation's exit status or prevent its planned writes or upload.

#### Scenario: Continue an install with findings during warning-only rollout

- **WHEN** an install plan contains high-confidence findings during warning-only rollout
- **THEN** LunaPack reports them and continues the install under existing lifecycle rules

#### Scenario: Continue publication with findings during warning-only rollout

- **WHEN** a pack contains findings during warning-only rollout
- **THEN** LunaPack reports them and publication continues under existing validation rules

### Requirement: Enforce high-confidence findings with an explicit override

After false-positive review enables enforcement, install, update, pack validation, and publication SHALL fail before their side effects when high-confidence findings exist unless the user supplies `--allow-hidden-characters`. The override SHALL not suppress retained findings or omission summaries, and warning-severity findings SHALL not block these operations.

#### Scenario: Block installation before mutation

- **WHEN** enforcement is enabled, an install plan contains a high-confidence finding, and no override is supplied
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

#### Scenario: Block publication before upload

- **WHEN** enforcement is enabled, a pack contains a high-confidence finding, and no override is supplied
- **THEN** LunaPack returns a non-success result before uploading the release

#### Scenario: Continue with an explicit override

- **WHEN** enforcement is enabled and a user supplies `--allow-hidden-characters`
- **THEN** LunaPack reports retained findings and omission summaries and continues the operation under its existing validation and transaction rules
