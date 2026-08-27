---
status: accepted
date: 2026-08-27
decision-makers: LunaPack maintainers
---

# ADR-0051: Normalize Links Into the Managed-Root Lifecycle

## Context and Problem Statement

Projects need selected files from local or Git repositories that do not publish
`pack.yml`. These selections need the same ownership, conflict detection,
rollback, audit, update, and uninstall protections as pack-managed files without
making project-owned definitions appear to be published packs.

## Decision Drivers

- Keep link configuration portable and resolved ownership reproducible.
- Reuse one managed-file safety and transaction boundary for packs and links.
- Preserve pack catalog, dependency, parameter, template, script, and trust behavior.
- Hash and copy bytes from one immutable operation snapshot.

## Considered Options

- Normalize packs and links into shared managed roots after pack resolution.
- Create synthetic pack manifests for links.
- Implement a separate link lifecycle.

## Decision Outcome

Chosen option: "Normalize packs and links into shared managed roots after pack
resolution", because ownership and transaction rules remain uniform while pack
semantics stay isolated.

Link definitions remain first-class project configuration. Link resolution
produces in-memory managed roots with explicit link ownership and source
evidence. Pack graphs enter the same model only after pack-specific resolution.
No synthetic manifest or version is persisted.

### Consequences

- Good, because every effective target has one ownership and conflict model.
- Good, because links inherit existing preflight, rollback, audit, and local-edit
  protections.
- Good, because Git commits and local bytes remain stable throughout planning
  and application.
- Bad, because lifecycle changes must run both pack regression tests and
  cross-owner link tests.
- Bad, because the Git link cache adds a separate untrusted cache boundary.

### Confirmation

Unit and process tests verify managed-root adaptation, pack/link collisions,
snapshot consistency, cache verification, rollback, audit, and uninstall.
Schemas confirm that project and lock files persist links separately from packs.

## Pros and Cons of the Options

### Normalize Into Shared Managed Roots

- Good, because lifecycle safety has one implementation.
- Bad, because pack-oriented planners require a more general owner model.

### Create Synthetic Pack Manifests

- Good, because existing pack inputs could be reused directly.
- Bad, because fake versions and pack-only behavior could leak into state or output.

### Implement a Separate Link Lifecycle

- Good, because link code would initially be isolated.
- Bad, because ownership, conflict, transaction, and audit rules would diverge.

## More Information

See [ADR-0016](0016-split-portable-configuration-from-resolved-lock-state.md),
[ADR-0037](0037-canonicalize-persisted-project-paths.md), and the
[Luna Links reference](../../../developer/cli/links.md).
