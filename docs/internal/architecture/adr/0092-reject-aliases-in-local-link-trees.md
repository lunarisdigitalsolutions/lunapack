---
status: accepted
date: 2026-11-20
decision-makers:
  - Lunaris Engineering
---

# ADR-0092: Reject Aliases in Local Link Trees

## Context and Problem Statement

Luna Links enumerate regular files from project-configured local directories.
Lexical containment does not protect that read when the source root, an
ancestor below the project, or a descendant is a symbolic link, junction, or
other reparse point. A recursive enumerator can otherwise enter an unrelated
directory and expose its regular files as locally selected link content.

ADR-0071 defines the mutation-side alias boundary and preserves warning-and-skip
behavior for unsupported children in operation-pack snapshots. Luna Links are a
selection and read boundary, so treating a child alias as an omitted file would
not preserve the complete, reviewed source selection.

## Decision Drivers

- A local link source must not read outside its configured project subtree.
- Child aliases must not turn an include selector into external disclosure.
- Enumeration must remain bounded and must not materialize an over-limit list.
- The read-time check must account for changes between listing and copying.
- Handle-relative no-follow traversal remains a portable implementation concern.

## Considered Options

- Skip reparse-point entries while keeping regular siblings.
- Resolve alias targets and allow those that remain under the source root.
- Reject alias components and descendants before reading file content.

## Decision Outcome

Chosen option: "Reject alias components and descendants before reading file
content," because it prevents deterministic path escape without making a
symlink target implicitly part of the configured source.

Luna validates every source-root component below the project directory, rejects
the root when it is an alias, and enumerates one directory level at a time.
Every encountered child alias aborts the listing. Immediately before copying a
selected file, Luna revalidates every component from the source root to that
file and rejects an alias introduced since listing.

This does not change ADR-0071's warning-and-skip policy for child aliases in
operation-pack snapshots.

### Consequences

- Good, because local link selections cannot deterministically disclose files
  through symlinked source ancestors or descendants.
- Good, because the operation fails before target mapping or managed-file
  planning instead of returning an incomplete selection.
- Bad, because local sources containing any selected or unselected alias fail
  until the alias is removed from the source tree.
- Bad, because path inspection and file opening remain separate operations, so
  a same-user process can still race component replacement.

### Confirmation

Unit tests set deterministic reparse-point attributes and verify rejection
during listing and materialization. Security tests create real directory links
and verify that source content outside the root is not enumerated or copied.
Symlink-creation tests are conditional on host support.

## More Information

This decision complements
[ADR-0071](0071-reject-filesystem-aliases-at-mutation-boundaries.md) and
implements the local-link confinement review for
[issue #83](https://github.com/lunarisdigitalsolutions/lunapack/issues/83).
Handle-relative no-follow opening remains future hardening for the documented
same-user race.
