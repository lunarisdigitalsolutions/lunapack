---
status: accepted
date: 2026-08-24
decision-makers: LunaPack maintainers
---

# ADR-0040: Secure Lifecycle Scripts With Scoped Trust

## Context and Problem Statement

Packs need lifecycle hooks for work that declarative managed files cannot
perform. Hooks execute publisher-controlled content with the user's authority,
may affect resources outside LunaPack, and may come from direct or transient
packs. Authorization and execution must bind to the same source content while
remaining explicit about filesystem and process-isolation limits.

## Decision Outcome

Each lifecycle declaration identifies either a packed file with an explicit
runner or a direct executable command. LunaPack resolves one executable,
displays escaped arguments, and launches it directly with shell execution
disabled.

Before authorization, LunaPack copies resolved pack content into an operation
snapshot. Packed files must remain beneath that snapshot and their digest is
verified immediately before execution. Trust binds to normalized configured
source identity and optionally a bare pack ID; source names alone never grant
trust. Project trust requires matching user-local acknowledgement. Script mode,
suppression, and graph-wide hook planning occur before any hook or managed-file
mutation.

LunaPack retains exact project configuration bytes, verifies the manifest
after every child process, and restores LunaPack-managed files, configuration,
and lock state after relevant failures. External effects from authorized code
are not transactional.

Snapshot materialization currently follows symbolic links, junctions,
mount-point redirects, and other reparse points encountered in source content.
It is not a confinement boundary against a same-user source-tree attacker.

No-follow traversal, regular-file validation, and source identity checks during
copying remain required before claiming immutable no-follow snapshots.

### Consequences

- Good, because lifecycle scripting can be delivered while the remaining
  filesystem work receives dedicated platform validation.
- Good, because trust identifies exact sources and process launch avoids
  implicit shell parsing.
- Good, because LunaPack-owned state is recoverable after hook failures.
- Bad, because source content may use links or reparse points to include data
  outside its apparent source directory.
- Bad, because digest verification binds the copied result, not the original
  source-tree traversal path.
- Bad, because authorized code retains the invoking user's ambient authority
  and can create irreversible external effects.

Typed schema, planner, policy, executor, and process tests cover hook forms,
ordering, scoped trust, source pinning, literal argv, manifest integrity,
failures, cancellation, and rollback. No-follow traversal, regular-file
validation, and source identity checks during copying remain required before
claiming immutable no-follow snapshots.
