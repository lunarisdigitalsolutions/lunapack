---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# Screen Pack-Managed Content for Hidden Unicode

## Context and Problem Statement

Pack-managed text can contain invisible or direction-changing Unicode
characters that reviews, diffs, filenames, and automated consumers can
misrepresent. The CLI needs one consistent policy for author validation,
publication, lifecycle planning, audit output, terminal-safe paths, and
untrusted binary or malformed text without scanning unrelated configuration or
lifecycle scripts.

## Decision Drivers

- Detect deceptive Unicode before pack content is published or applied.
- Keep legitimate emoji sequences usable while warning on suspicious contexts.
- Do not let NUL bytes, invalid UTF-8, or BOM ambiguity suppress recoverable
  controls.
- Prevent hidden or control characters in paths from spoofing terminal output.
- Bound diagnostic memory and output for hostile payloads.
- Preserve an explicit, non-persistent escape hatch for reviewed cases.
- Keep audit read-only and report-only.

## Considered Options

- Block every Unicode format character.
- Report every finding as a warning without enforcement.
- Skip every NUL-containing or invalid UTF-8 payload.
- Fail every opaque payload as unscannable.
- Block a narrow high-confidence set, warn on other format characters, recover
  controls from opaque encodings, reject unsafe paths, and provide an explicit
  override.

## Decision Outcome

Chosen option: "Block a narrow high-confidence set, warn on other format
characters, recover controls from opaque encodings, reject unsafe paths, and
provide an explicit override," because it closes review and terminal-output
bypasses without rejecting opaque content solely for its encoding.

The scanner streams strict UTF-8 decoding and resynchronizes after invalid
bytes without inserting replacement characters. It recognizes UTF-8, UTF-16LE,
UTF-16BE, UTF-32LE, and UTF-32BE leading BOMs and decodes BOM-tagged content
one Unicode scalar at a time. Ambiguous UTF-32LE/UTF-16LE bytes are checked with
both valid decodings when the UTF-32 interpretation is malformed. NUL and
invalid bytes do not fail an operation alone, but later recoverable hidden
controls remain enforceable. For recovery locations, each invalid byte occupies
one scan position.

Unicode `Bidi_Control` characters and U+200B ZERO WIDTH SPACE are
high-confidence findings. Other Unicode `Format` characters and orphan
variation selectors are warnings. Valid emoji ZWJ, variation, and keycap
sequences remain unreported.

Install and update screen final planned bytes after rendering and merge
planning. Author validation and publication screen raw selected source bytes,
including external sources. Audit screens existing managed targets. Findings
use slash-only relative paths, 1-based lines, Unicode-scalar columns, code
points, and severity.

Install, update, `luna pack validate`, and `luna publish` fail before side
effects when high-confidence findings exist. `--allow-hidden-characters`
continues an explicitly reviewed operation without suppressing retained
findings or omission summaries; warning findings do not block. Audit findings
never change audit status or mutate state. The override is not persisted.

Project-relative and managed manifest paths reject terminal controls, Unicode
`Format` characters, line separators, and paragraph separators. Diagnostic
formatting visibly escapes unsafe legacy path characters, quotes, and
backslashes so they cannot alter terminal output.

The scanner retains at most 1,000 findings per payload, reports the omitted
count, and keeps separate evidence for a high-confidence finding. If an
otherwise omitted blocker arrives, it replaces one retained warning so the
diagnostic remains actionable.

### Consequences

- Good, because deceptive direction controls and zero-width spaces cannot enter
  managed content through recoverable encodings without explicit review.
- Good, because path values cannot inject terminal controls or hidden direction
  formatting through normal pack boundaries.
- Good, because valid emoji sequences remain usable and warning classifications
  preserve review visibility.
- Good, because diagnostic memory remains bounded for hostile files.
- Bad, because unusual opaque encodings can produce conservative warning or
  recovery locations rather than a replacement-decoder interpretation.
- Bad, because legitimate filenames containing Unicode format characters,
  including emoji joiners, are rejected.
- Bad, because embedded Extended_Pictographic ranges require deliberate review
  when Unicode data changes.

### Confirmation

Unit, integration, and security tests cover NUL and invalid UTF-8 recovery,
BOM-tagged UTF-16 and UTF-32, ambiguous encodings, newline and scalar positions,
every `Bidi_Control` blocker, U+200B, orphan selectors, emoji sequences,
terminal-control paths, diagnostic escaping, and a blocker after the
1,000-finding limit. Lifecycle, authoring, publication, and audit tests verify
pre-side-effect failure, override behavior, and state preservation. A Release
CLI smoke test verified blocked installation, allowed installation, and
read-only audit. The implementation baseline reviewed 157 maintained pack files
with no hidden-format candidates before enforcement.

## Pros and Cons of the Options

### Block every format character

- Good, because it is simple and catches every Unicode format code point.
- Bad, because it rejects legitimate text uses and gives no reviewed exception.

### Report warnings only

- Good, because it cannot introduce new lifecycle failures.
- Bad, because known-deceptive content can still be published or installed.

### Skip every opaque payload

- Good, because binary content is never decoded.
- Bad, because recoverable controls can bypass enforcement.

### Fail every opaque payload

- Good, because every unscannable payload receives fail-closed treatment.
- Bad, because harmless binary and invalid UTF-8 content becomes incompatible
  with the managed-file contract.

### Recover controls, harden paths, and bound diagnostics

- Good, because it closes encoding and terminal-path bypasses without failing
  opaque content solely for its encoding.
- Good, because blocker evidence and bounded output survive hostile inputs.
- Bad, because unusual legitimate filenames and Unicode data require explicit
  compatibility review.

## More Information

Revisit the blocker set and per-payload diagnostic limit only with
representative false-positive, false-negative, memory, or output evidence.
`--allow-hidden-characters` is an incident-review mechanism, not a general
replacement for pack review or malware analysis.
