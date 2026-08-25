---
status: accepted
date: 2026-08-21
decision-makers: LunaPack maintainers
---

# ADR-0027: Require Explicit Template Rendering

## Context and Problem Statement

Treating every managed file as Scriban content rejects literal template-like
files and makes binary-safe copying impossible.

## Decision Outcome

Chosen option: "Add a per-selector `template` flag that defaults to false",
because pack authors know which sources require parameter expansion and explicit
configuration avoids content heuristics.

### Consequences

- Literal and non-UTF-8 sources copy unchanged by default.
- Template parsing remains opt-in and strict.
- Existing parameterized packs must declare `template: true`.

### Confirmation

Renderer tests prove disabled template handling preserves literal Scriban-like
content, while enabled templates retain strict variable validation.

## More Information

- [Pack manifest reference](../../../developer/packs/reference/manifest.md)
