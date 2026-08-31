---
status: superseded by ADR-0066
date: 2026-08-30
decision-makers:
  - Lunaris Engineering
---

# ADR-0065: Organize Packs by Release and Payload Purpose

## Context and Problem Statement

Maintained packs previously placed one mutable manifest directly below each
pack ID and stored most payloads in a generic `templates/` directory. That
layout obscured release immutability and whether a source represented a whole
target, a merge fragment, an instruction, or executable lifecycle code.

## Decision Drivers

- Keep every exact release resolvable after later versions are published.
- Make ownership intent visible during authoring and review.
- Keep source paths predictable across maintained packs.
- Provide a minimal starting point without requiring authors to design a
  directory layout first.

## Considered Options

- Keep one mutable manifest and a generic payload directory per pack.
- Version manifests but retain generic payload directories.
- Version complete release roots and group payloads by purpose.

## Decision Outcome

Chosen option: "Version complete release roots and group payloads by purpose,"
because it preserves exact releases and exposes ownership intent without adding
manifest semantics.

### Consequences

- Maintained releases live at `<pack-id>/<version>/pack.yml` with all release
  content below the same version directory.
- Complete managed files use `targets/` and mirror their default project path.
- Merge inputs use `fragments/<target>/` and identify their contributing pack.
- Lifecycle guidance uses `instructions/`; executable lifecycle files use
  `scripts/`; isolated lifecycle workspaces use `fixtures/`; non-installed
  samples use `examples/`.
- Empty purpose directories are omitted.
- The `lunapack-pack-authoring` pack installs a minimal manifest and managed
  example file that authors must customize.
- Catalog discovery remains recursive; directory names explain intent but do
  not select content.

### Confirmation

Repository review confirms every maintained manifest is at
`projects/packs/<pack-id>/<version>/pack.yml`, selectors resolve inside that
release, and no maintained payload uses `templates/`. Bundled catalog discovery
and pack validation tests confirm all releases remain valid and discoverable.
