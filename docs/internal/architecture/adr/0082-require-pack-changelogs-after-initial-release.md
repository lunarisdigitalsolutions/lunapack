---
status: accepted
date: 2026-09-06
decision-makers:
  - Lunaris Engineering
---

# ADR-0082: Require Pack Changelogs After Initial Release

## Context and Problem Statement

Maintainer policy required a changelog for every maintained pack without
distinguishing an initial release from a version update. An initial release has
no earlier consumer contract to compare, so its manifest and release evidence
already describe the complete shipped behavior.

## Decision Drivers

- Keep release evidence proportionate to the information consumers need.
- Record changes between versions without duplicating an initial manifest.
- Apply one predictable rule across maintained packs.

## Considered Options

- Require a changelog for every release, including the initial version.
- Require changelog entries only after the initial release.
- Make changelogs optional for all pack releases.

## Decision Outcome

Chosen option: "Require changelog entries only after the initial release,"
because updates need an explicit comparison with prior behavior while an
initial release does not.

### Consequences

- Initial pack releases need ownership, a semantic version, and release
  evidence, but no changelog entry.
- Every later pack version records its externally relevant changes.
- Reviewers determine whether a release is initial from the absence of an
  earlier version in the maintained catalog.

### Confirmation

Publication review checks for a changelog entry when a maintained pack already
has an earlier released version.
