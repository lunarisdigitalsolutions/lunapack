---
status: accepted
date: 2026-08-31
decision-makers:
  - Lunaris Engineering
---

# ADR-0071: Reject Filesystem Aliases at Mutation Boundaries

## Context and Problem Statement

ADR-0040 introduced operation snapshots and scoped lifecycle trust but accepted
that snapshot copying followed links and reparse points. Project-relative path
validation was also lexical: an existing symbolic link, junction, or other
reparse-point ancestor could redirect a mutation outside the workspace, while
writing through a hard-linked target could modify another directory entry.

This decision supersedes ADR-0040. Its lifecycle declaration, trust,
authorization, direct-process, digest-verification, and rollback decisions
remain unchanged; this record replaces its filesystem-alias exception.

## Decision Drivers

- Project-relative mutations must not traverse existing filesystem aliases.
- A rejected action must not leave an earlier action applied.
- Unsupported source entries must not block regular sibling content.
- Existing hard-linked targets must be replaceable without changing other links.
- The implementation must remain portable and Native AOT compatible.

## Considered Options

- Continue lexical validation and document alias risks.
- Reject unsupported snapshots and existing mutation aliases by path inspection.
- Implement handle-relative, race-free traversal on every platform immediately.

## Decision Outcome

Chosen option: "Reject unsupported snapshots and existing mutation aliases by
path inspection," because it closes deterministic alias escapes with portable
APIs while preserving a separate path to race-resistant native traversal.

Operation snapshot roots must not be links or reparse points. During recursive
copy, LunaPack warns and skips child links, reparse points, devices, and other
unsupported entries, then continues with regular siblings.

Before a transaction mutates any target, LunaPack validates every action path
and existing ancestor below the workspace. Direct move, uninstall, rollback,
and state-restoration paths use the same boundary. Existing alias ancestors
abort the operation before mutation.

Writes create a unique sibling file and move it over the destination. This
replaces a hard-linked destination entry with a new file identity instead of
truncating the shared file identity.

### Consequences

- Good, because existing links and reparse-point ancestors cannot redirect a
  validated project mutation outside its workspace.
- Good, because replacing a hard-linked target preserves other directory entries.
- Good, because unsupported snapshot children produce visible diagnostics while
  regular files remain usable.
- Bad, because skipped source entries may make a pack incomplete.
- Bad, because path inspection and later mutation remain separate operations.
  A same-user process can still race an entry replacement.

### Confirmation

Unit tests must cover deterministic reparse-point rejection before mutation,
duplicate and malformed document input, and warning-plus-skip snapshot behavior.
Real-filesystem security tests must cover symbolic-link escape attempts and
hard-linked target replacement. Symlink tests run on hosts that permit link
creation; all supported operating-system families remain required before the
race limitation can be reconsidered.

## More Information

Handle-relative no-follow traversal remains future hardening. On Unix it needs
directory-relative operations and no-follow flags; on Windows it needs stable
handle identity and reparse-point controls. LunaPack does not claim race-free
confinement against another process running as the same user.
