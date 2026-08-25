---
status: accepted
---

# ADR-0016: Split Portable Configuration from Resolved Lock State

## Context and Problem Statement

The original version-1 `lunapack.yml` mixed a project's selected sources and pack
roots with machine-specific resolved paths, managed-file ownership, and content
digests. That shape was not portable and could not describe ownership across a
composite pack graph unambiguously.

Composite pack manifests also need exact dependencies without allowing a pack
author to select the consumer's source or trust boundary.

## Decision Drivers

- Keep project intent portable and reviewable.
- Record reproducible resolved provenance and lifecycle ownership.
- Preserve source-order resolution while keeping source selection with consumers.
- Avoid maintaining a compatibility or migration path for an obsolete state model.

## Considered Options

- Retain the combined version-1 project document.
- Version `lunapack.yml` and provide migration support.
- Split portable configuration from resolved lock state without a configuration version increment.

## Decision Outcome

Chosen option: "Split portable configuration from resolved lock state without a
configuration version increment", because it separates durable user intent from
generated ownership while keeping the published configuration contract small.

`lunapack.yml` remains schema version `1` and contains only relative local sources
and requested root packs. `lunapack-lock.yml` begins at schema version `1` and
contains every resolved pack, its source-relative provenance, exact dependency
edges, managed target paths, and SHA-256 digests. Composite manifests carry
only exact `id@version` references; configured consumers select sources.

The previous combined version-1 document shape is intentionally invalid. LunaPack
does not provide automatic conversion or a migration command. Consumers must
recreate project state using the portable configuration and regenerated lock
file.

### Consequences

- Good, because configuration can move with a project without machine-specific paths.
- Good, because lifecycle operations can preserve shared composite dependencies safely.
- Good, because source choice and trust remain consumer-controlled.
- Bad, because the two documents must be written and recovered as one state change.
- Bad, because users of the prior combined document must recreate state manually.

### Confirmation

Schema, unit, and process tests validate relative sources, exact composite
graphs, lock ownership, shared dependency removal, and rollback. The CLI
rejects legacy combined documents through the schema validator.

## Pros and Cons of the Options

### Retain the combined version-1 project document

- Good, because consumers would keep one familiar file.
- Bad, because it mixes portable intent with generated, machine-specific ownership.

### Version `lunapack.yml` and provide migration support

- Good, because the incompatible document shape would be explicit in a version number.
- Bad, because it adds a migration contract and compatibility path before either is needed.

### Split portable configuration from resolved lock state without a configuration version increment

- Good, because each document has one authority and clear lifecycle purpose.
- Bad, because incompatible older version-1 files need explicit recreation guidance.

## More Information

The implementation and acceptance criteria are recorded in the
[composite-pack lock-file OpenSpec change](../../../../openspec/changes/archive/2026-08-16-composite-pack-lockfile/design.md).
