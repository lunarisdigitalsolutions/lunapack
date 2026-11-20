# Design

## Context

See `proposal.md` and `specs/local-pack-lifecycle/spec.md` for motivation and
observable behavior. `PackUpdatePlanner` currently creates delete actions for
prior targets absent from the new resolved graph without comparing the current
file digest with the lock-file digest. `PackUpdateTransaction` validates action
paths, snapshots files for rollback, and deletes targets unconditionally.
`PackLifecycleService` builds the next lock ownership from the new installation
plan, so omitted targets naturally lose stale ownership. No lock-file schema
change is needed. Pack and link updates share the same typed deletion
transaction, so every deletion carries a planning-time content precondition.

## Goals / Non-Goals

**Goals:**

- Preserve modified obsolete targets by default and expose deliberate removal
  through `--remove-modified`.
- Make planning, dry-run output, warnings, and transaction behavior agree on
  each effective target path.
- Detect target changes between planning and deletion and preserve transaction
  rollback behavior.
- Keep existing project-path security checks in the deletion path.

**Non-Goals:**

- Change update strategies for targets still owned by the new resolved graph.
- Keep preserved obsolete targets under pack ownership or add lock-file fields.
- Add a general force-delete option for files outside obsolete managed targets.

## Decisions

### Compare current content with prior ownership

Use the existing SHA-256 mechanism to compare each obsolete target's current
bytes with the prior lock-file digest. Delete by default only when the digest
matches every prior owner of the effective path. Treat a missing target as
already removed: emit no filesystem mutation and let the next lock state omit
its stale ownership. An unverifiable or inconsistent shared digest is not safe
for default deletion, so preserve the target.

Alternative: continue deleting unconditionally, which can discard local edits.
Alternative: fail the whole update whenever an obsolete target is modified,
which blocks unrelated updates. Preservation is safer and lets other updates
complete.

### Represent preservation in the update plan

Add a non-mutating planned action for a modified obsolete target. Reuse the
planned-action pipeline so dry-run output can identify preservation alongside
creates, updates, and deletes. After state persistence, emit one warning per
preserved effective path during a real update; dry-run reports the planned
preservation without changing files or state.

Alternative: emit only a log warning. That would leave dry-run output unable to
show the decision in the file-change list and would duplicate planner logic in
the formatter.

### Separate locked digest from planned on-disk digest

A delete action must retain the prior lock digest for ownership comparison and
the current on-disk digest observed during planning for transaction validation.
These values differ when `--remove-modified` allows a modified target to be
deleted. Immediately before deletion, revalidate the action path and compare
the current digest or expected absence with the planned observation. On
mismatch, fail the transaction so earlier file changes are restored. If a
target was absent during planning, create no deletion action; a file created
after planning therefore cannot be deleted by that plan.

Alternative: reuse the lock digest for the transaction check. This would make
the explicit override unusable for modified targets and would not detect a new
file at a previously absent path.

Deletion preconditions are mandatory for every producer, including Luna Links.
Link planners preserve modified obsolete targets and do not create deletion
actions for absent targets. The `--remove-modified` override remains
pack-update-only and is rejected for a named link before mutation. Named link
updates also reject `--dry-run` until a link preview path exists, preventing
the shared update command from mutating through the real link lifecycle path.

### Group shared owners by effective path

Group prior owners using the normalized project-relative effective target
path. Skip removal when any new planned owner still uses that path. Otherwise,
create at most one preserve or delete action for the group. Without the
override, delete only if current content matches every prior owner's digest;
with the override, allow the modified target to be deleted once. This avoids
duplicate deletes and warnings for shared merge targets.

Use `ProjectPath.NormalizeProjectRelativePath` and the host filesystem comparer
for target identity. This resolves `.` segments and applies Windows
case-insensitive semantics before grouping, chaining, lock updates, or rollback
state tracking.

Alternative: process each owner independently. That can schedule duplicate
deletes for one file and can make behavior depend on owner iteration order.

### Keep lock ownership aligned with the new graph

Continue generating managed-file lock entries from the new installation plan.
A preserved obsolete file remains on disk but becomes user-owned; the next
successful lock file contains no stale owner for it. No schema migration or
special lock entry is added. An explicit command remapping becomes the
effective lock target; the prior locked path is retained only when no command
remap selected a replacement.

Alternative: keep the previous lock entry so future updates can manage the
preserved file. The user selected ownership loss, and retaining the entry would
make future update behavior ambiguous.

## Risks / Trade-offs

- Preserved files are no longer managed by LunaPack. The warning and developer
  documentation must make this ownership change explicit.
- A separate process could modify a file in the narrow interval after the
  transaction digest check. Revalidate immediately before deletion and retain
  the existing path-alias checks; digest verification and path-based deletion
  remain separate operations.
- A shared target with inconsistent prior digests is preserved by default.
  This favors user data over automatic cleanup; `--remove-modified` remains an
  explicit option for that obsolete target.

## Migration Plan

No lock-file migration is required. Existing lock digests provide the
comparison baseline. The new CLI changes only the default handling of modified
obsolete targets; `--remove-modified` restores explicit deletion when desired.
Older CLI versions retain their existing unconditional-removal behavior, so
users should use the updated CLI when relying on preservation.
