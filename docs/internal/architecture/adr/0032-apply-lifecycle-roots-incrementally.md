---
status: accepted
date: 2026-08-21
decision-makers: Lunaris Engineering
---

# ADR-0032: Apply Lifecycle Roots Incrementally

## Context and Problem Statement

Install previously resolved and planned every configured root when adding one
pack. Required parameters from an unrelated installed root could block the new
installation, and planning could rewrite or remove existing managed targets.
Commands also need to accept multiple pack references without reapplying a
shared transient pack.

## Decision Drivers

- Keep lifecycle changes scoped to the requested roots.
- Preserve lockfile ownership and managed-target integrity.
- Reuse compatible transitive packs without hidden reinstallation.

## Considered Options

- Resolve and plan the complete configured graph for every command.
- Incrementally resolve each requested root and merge compatible lock entries.
- Treat all command references as one atomic batch.

## Decision Outcome

Chosen option: "Incrementally resolve each requested root and merge compatible
lock entries", because it avoids unrelated parameter resolution while retaining
the lockfile as the source of truth for already installed packs.

### Consequences

- Install excludes packs already present in the lockfile at the same version.
- A newly resolved version that conflicts with a locked package fails before
  managed files change.
- Install, update, and uninstall process supplied references in command-line
  order. A later failure does not roll back earlier successful references.
- Incremental installation does not delete targets owned by prior roots and
  refreshes the recorded digest for every owner of a changed shared target.

### Confirmation

Unit tests cover scoped parameter prompting, repeated lifecycle references, and
shared transient lock reuse. The CLI unit-test project validates the behavior.

## More Information

This refines parameter binding in [ADR-0017](0017-bind-pack-parameters-before-rendered-ownership.md)
and strategy-aware update planning in
[ADR-0018](0018-plan-strategy-aware-pack-updates.md).
