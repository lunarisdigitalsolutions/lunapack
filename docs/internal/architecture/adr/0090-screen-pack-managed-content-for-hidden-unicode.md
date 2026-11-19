---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# Screen Pack-Managed Content for Hidden Unicode

## Context and Problem Statement

Pack-managed text can contain invisible Unicode formatting or direction-changing
characters that are difficult to notice during review. The CLI needs one
consistent policy for author validation, publication, lifecycle planning, and
audit without scanning unrelated configuration or lifecycle scripts.

## Decision Drivers

- Detect deceptive Unicode before pack content is published or applied.
- Avoid rejecting legitimate emoji sequences and common authored text.
- Keep invalid UTF-8 and binary payloads outside text-screening behavior.
- Preserve a deliberate escape hatch for reviewed high-confidence findings.
- Keep audit read-only and report-only.

## Considered Options

- Block every Unicode format character.
- Report every finding as a warning without enforcement.
- Block a narrow high-confidence set, warn on other format characters, and
  provide an explicit command-scoped override.

## Decision Outcome

Chosen option: "Block a narrow high-confidence set, warn on other format
characters, and provide an explicit command-scoped override," because it
protects mutating and release operations while limiting false positives.

The CLI screens valid UTF-8 bytes selected as managed content. It skips payloads
containing NUL bytes and payloads that strict UTF-8 decoding rejects. Unicode
`Bidi_Control` characters and U+200B ZERO WIDTH SPACE are high-confidence
findings. Other Unicode format characters are warnings. Valid emoji zero-width
joiner sequences and variation selector sequences remain unreported.

Install and update screen final planned bytes after rendering and merge
planning. Author validation and publication screen the raw source selected by
managed-file declarations, including resolved external sources. Audit screens
the existing installed managed targets. Findings use project- or source-relative
slash paths, 1-based lines, 1-based Unicode-scalar columns, code points, and
severity.

Install, update, `luna pack validate`, and `luna publish` fail before their
side effects when a high-confidence finding is present.
`--allow-hidden-characters` continues the operation without suppressing
diagnostics. Warning
findings never block these operations, and audit findings never change audit's
exit status. The override is not persisted.

### Consequences

- Good, because deceptive direction controls and zero-width spaces cannot enter
  through managed content without explicit review.
- Good, because emoji joiners and selectors do not generate false positives.
- Good, because all supported operations share one scanner, severity model, and
  diagnostic format.
- Bad, because binary payloads without a NUL byte can be misclassified as text
  and may produce format findings.
- Bad, because the embedded emoji property ranges require deliberate review
  when Unicode data is updated.

### Confirmation

Unit tests cover strict UTF-8 and NUL skipping, newline and Unicode-scalar
positions, every `Bidi_Control` blocker, U+200B, warning-only format characters,
emoji joiners, and variation selectors. Lifecycle, authoring, publication, and
audit tests cover diagnostics, pre-side-effect failure, override behavior, and
state preservation. The implementation baseline reviewed 157 maintained pack
files with no hidden-format candidates before enforcement.

## Pros and Cons of the Options

### Block every format character

- Good, because it is simple and catches every Unicode format code point.
- Bad, because it rejects legitimate text uses and gives no escape path for
  reviewed cases.

### Report warnings only

- Good, because it cannot introduce new lifecycle failures.
- Bad, because known-deceptive content can still be published or installed.

### Block a narrow high-confidence set with an override

- Good, because it protects known-deceptive cases with low false-positive risk.
- Good, because warnings retain visibility for less certain characters.
- Bad, because unusual format characters can remain non-blocking.

## More Information

Revisit the high-confidence set when maintained-pack evidence identifies a
repeatable false positive or a false negative that cannot be handled by the
command-scoped override. Re-evaluate the text/binary heuristic if repository
evidence shows NUL-free binary payloads are common in managed content.
