---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# Fail Safe Hidden-Unicode Screening

## Context and Problem Statement

Security review found that treating every NUL-containing or invalid UTF-8 payload
as binary can let recoverable bidi bytes avoid hidden-Unicode enforcement.
Unescaped terminal controls and hidden format characters in managed paths can
also spoof diagnostics. A large file made entirely of findings can amplify
memory and output use unless diagnostic retention is bounded.

## Decision Drivers

- Do not let opaque bytes neutralize recognized hidden controls.
- Preserve the warning/block decision for valid text and emoji sequences.
- Keep invalid or binary payloads non-blocking by themselves.
- Prevent control characters and bidi formatting in paths from spoofing
  terminal output.
- Bound scanner and diagnostic memory independently of untrusted file size.

## Considered Options

- Preserve the original UTF-8/NUL skip heuristic and document the limitation.
- Fail every NUL-containing or invalid Unicode payload as unscannable.
- Recover recognized Unicode controls, decode BOM-tagged Unicode, reject unsafe
  paths, and bound retained diagnostics.

## Decision Outcome

Chosen option: "Recover recognized Unicode controls, decode BOM-tagged Unicode,
reject unsafe paths, and bound retained diagnostics," because it closes the
demonstrated bypass without rejecting opaque payloads solely for their encoding.

The scanner streams strict UTF-8 decoding and resynchronizes after invalid
bytes. It never replaces invalid bytes in the reported content, but later valid
Unicode scalars remain eligible for classification. A leading UTF-8 BOM is
ignored as encoding metadata. Leading UTF-16LE, UTF-16BE, UTF-32LE, and UTF-32BE
BOMs select the corresponding strict decoder. NUL bytes do not suppress
screening.

Project-relative and managed manifest paths reject terminal control characters,
Unicode `Format` characters, line separators, and paragraph separators.
Diagnostic formatting visibly escapes controls, format characters, quotes, and
backslashes so legacy persisted paths cannot alter terminal output.

The scanner retains at most 1,000 findings per payload. It counts omitted
findings, reports one omission summary, and preserves a separate
high-confidence-seen flag so an omitted blocker cannot bypass enforcement.

### Consequences

- Good, because recognized bidi controls and zero-width spaces remain visible in
  NUL-containing, invalid UTF-8, and BOM-tagged Unicode text.
- Good, because path values cannot inject terminal control or hidden direction
  formatting through normal pack and project boundaries.
- Good, because memory for diagnostic retention stays bounded for hostile
  payloads.
- Bad, because paths containing legitimate Unicode format characters such as
  emoji joiners are rejected.
- Bad, because locations following invalid UTF-8 bytes count each recovered
  invalid byte as one scan position rather than representing a Unicode scalar.

### Confirmation

Unit tests cover NUL-containing, invalid UTF-8, UTF-16, UTF-32, terminal escape
paths, bidi paths, orphan variation selectors, and a high-confidence finding
after the 1,000-finding retention limit. End-to-end lifecycle, author validation,
and publication tests verify that opaque payload controls block before side
effects.

## Pros and Cons of the Options

### Preserve the original skip heuristic

- Good, because no existing behavior changes.
- Bad, because recoverable controls can bypass enforcement.
- Bad, because malicious path output remains available to terminal injection.

### Fail every opaque payload

- Good, because every unscannable payload receives fail-closed treatment.
- Bad, because harmless binaries and invalid UTF-8 text become incompatible
  with the accepted pack-content contract.

### Recover controls and harden paths

- Good, because it closes the bypass without failing opaque content by itself.
- Good, because diagnostics remain bounded and terminal-safe.
- Bad, because recovery locations after invalid bytes are best represented as
  one scan position per invalid byte.

## More Information

This decision supersedes ADR-0090. Revisit the per-payload diagnostic limit only
with measured memory and output evidence from representative pack content.
