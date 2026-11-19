---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0090: Protect Modified Targets Removed by Pack Updates

## Context and Problem Statement

Pack updates reconcile the previous resolved graph with the next graph. When a
new release stops declaring a managed target, the old update plan deleted that
path without comparing its current content with the ownership digest in
`lunapack-lock.yml`. A local edit could therefore be removed during an otherwise
successful update.

This decision extends [ADR-0005](0005-protect-local-managed-files.md) to
obsolete pack and Luna Link targets removed by updates and operates within the
transactional plan defined by
[ADR-0018](0018-plan-strategy-aware-pack-updates.md). Path
confinement continues to follow
[ADR-0071](0071-reject-filesystem-aliases-at-mutation-boundaries.md).

## Decision Drivers

- Preserve user edits when prior managed ownership is no longer applicable.
- Keep the update graph transaction complete rather than fail unrelated work.
- Make destructive cleanup explicit and visible in dry-run output.
- Revalidate planned deletion content immediately before mutation.
- Handle shared effective target paths once and deterministically.
- Preserve the existing lock-file schema.

## Considered Options

- Preserve modified obsolete targets by default and add an explicit removal
  override.
- Fail the complete update when an obsolete target is modified.
- Continue deleting obsolete targets without digest protection.

## Decision Outcome

Chosen option: "Preserve modified obsolete targets by default and add an
explicit removal override," because it protects local content while allowing an
update to complete and gives users an explicit, auditable cleanup path.

The update planner groups prior ownership by normalized effective target path.
If a target remains in the next plan, including through another merge owner,
the path is retained. A missing obsolete target produces no file action and its
stale lock ownership is omitted. An existing obsolete target is deleted only
when its current SHA-256 matches every prior owner's locked digest.

When at least one prior digest differs, LunaPack adds a non-mutating preserve
action by default. Real updates warn that the file is retained and no longer
managed by LunaPack; the successful next lock state contains no stale owner.
Dry runs report the same decision.

`luna update --remove-modified` allows a modified obsolete target to receive a
delete action for packs. The flag does not affect links or a target still owned
by the new graph. Every delete action, including link updates and removals,
records the bytes observed during planning. Before mutation, the transaction
revalidates path aliases and digest equality; a changed or missing planned
target fails the update and triggers rollback.

### Consequences

- Good, because obsolete modified files are not silently deleted.
- Good, because users can preview and explicitly opt into deletion.
- Good, because shared merge targets are evaluated once by effective path.
- Good, because no lock-file schema migration is required.
- Bad, because a preserved file becomes user-owned and LunaPack no longer
  reconciles it through the previous pack owner.
- Bad, because path inspection and deletion remain separate operations; the
  existing same-user race limitation from ADR-0071 remains.

### Confirmation

Planner tests cover unchanged, modified, forced, missing, shared, aliased,
ignored, and still-planned paths. Transaction tests cover post-planning edits,
missing planned targets, override deletion, rollback identity, and repeated
actions. Command and lifecycle tests verify warnings after persistence, dry-run
decisions, remap ownership, and stale pack or link ownership removal. Security
review confirms every delete path remains guarded by
`ProjectMutationPathSecurity`.

## Pros and Cons of the Options

### Preserve modified obsolete targets and add an explicit override

- Good, because unrelated updates can complete without destroying user edits.
- Good, because destructive cleanup remains discoverable and previewable.
- Bad, because preserved files become unmanaged.

### Fail the update for every modified obsolete target

- Good, because no lock transition occurs until the user resolves the file.
- Bad, because one obsolete local edit blocks otherwise independent updates.

### Continue unconditional deletion

- Good, because obsolete-file cleanup remains simple.
- Bad, because the operation can destroy locally modified content.

## More Information

Issue #79 defines the reported update-removal loss case. Confirmation must use
the focused planner and transaction tests plus the documented CLI unit,
integration, and security suites.
