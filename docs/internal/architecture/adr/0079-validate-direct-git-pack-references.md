---
status: accepted
date: 2026-09-02
decision-makers:
  - Lunaris Engineering
---

# ADR-0079: Validate Direct Git Pack References

## Context and Problem Statement

`luna validate` inspected raw local-source candidates, including invalid
manifests, but skipped every Git source. A valid draft pack from Git could be
installed or inspected by exact reference yet appeared unavailable to
validation. Direct validation must follow configured source support without
making drafts visible in default discovery or search results.

## Decision Drivers

- Validate an exact pack reference consistently across local and Git sources.
- Keep detailed diagnostics for invalid local manifests and source files.
- Preserve the default draft filtering of discovery and search.
- Reuse Git ref resolution, caching, validation, and provenance boundaries.

## Considered Options

- Combine raw local validation with validated Git catalog candidates.
- Route every validation candidate through the catalog.
- Keep validation local-only and add draft visibility options.

## Decision Outcome

Chosen option: "Combine raw local validation with validated Git catalog
candidates," because it adds Git support through the existing source browser
without losing local invalid-manifest diagnostics.

### Consequences

- Direct exact and latest validation can select valid draft packs from Git.
- Discovery and search continue to hide drafts unless `--allow-draft` is used.
- Local candidates retain detailed validation issues even when invalid.
- Git candidates excluded by Git catalog validation remain unavailable rather
  than exposing partial remote validation results.
- Git validation may resolve a remote ref and refresh the project-local cache.

### Confirmation

Focused tests validate an exact draft release from a Git source and retain local
latest-version and missing-file diagnostics. CLI command documentation keeps
draft visibility distinct from direct reference resolution.

## Pros and Cons of the Options

### Combine Raw Local Validation with Validated Git Catalog Candidates

- Good, because each source uses its established validation boundary.
- Good, because local invalid manifests remain diagnosable.
- Bad, because candidate gathering has separate local and Git paths.

### Route Every Validation Candidate Through the Catalog

- Good, because all source types share one candidate list.
- Bad, because invalid local manifests are removed before their issues can be
  reported.

### Keep Validation Local-Only and Add Draft Visibility Options

- Good, because implementation remains unchanged.
- Bad, because draft visibility does not address Git source exclusion.

## More Information

This decision extends the source-dispatched catalog adopted by
[ADR-0014](0014-adopt-source-dispatched-pack-catalog.md).
