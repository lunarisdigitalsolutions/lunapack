---
status: accepted
date: 2026-08-27
decision-makers: LunaPack maintainers
---

# ADR-0055: Extend Recoverable Lifecycle Hooks Through Uninstall

## Context and Problem Statement

Lifecycle hooks covered installation and update, but packs could not run cleanup
work during uninstall. Post-mutation hooks also ran before state persistence. A
hard interruption in that window could leave new files on disk while the lock
still described the old owners. Uninstall must remain possible when the source
that supplied an installed release is no longer available.

## Decision Drivers

- Support ordered cleanup instructions and scripts around managed-file removal.
- Execute hooks from the exact installed release rather than a newer catalog entry.
- Preserve uninstall as a recovery operation when source content is unavailable.
- Keep managed files, configuration, and lock ownership coherent across interruption.
- Restore pre-operation state after handled post-hook failures.

## Considered Options

- Add uninstall hooks, resolve the locked release, and checkpoint before post hooks.
- Run uninstall hooks from the latest available release.
- Persist state only after post hooks and accept the interruption window.
- Require source availability before uninstalling.

## Decision Outcome

Chosen option: "Add uninstall hooks, resolve the locked release, and checkpoint
before post hooks", because hook behavior must match installed content while
LunaPack ownership remains recoverable after interruption.

The ordered hook model includes `preUninstall` and `postUninstall`. LunaPack
materializes the exact releases recorded by the installed graph, prepares and
authorizes all applicable hooks, runs pre-uninstall hooks, removes managed
content, persists the resulting configuration and lock checkpoint, then runs
post-uninstall hooks. Dependencies retain the lifecycle planner's stable
dependency-first order, and declaration order remains significant.

Install and update use the same checkpoint boundary before post hooks. A handled
post-hook failure restores previous managed files, configuration, and lock state.
External script effects remain irreversible. A hard process interruption after
the checkpoint leaves the newly applied ownership state rather than stale lock
ownership.

If the exact installed graph cannot be materialized, LunaPack warns, skips
uninstall hooks, and continues removing LunaPack-owned content and state. It
never substitutes hooks from another release.

This decision supersedes ADR-0053 while retaining its ordered typed-hook,
authorization, suppression, and instruction-display decisions.

### Consequences

- Good, because packs can provide cleanup work without weakening script trust.
- Good, because interrupted post hooks do not leave newly mutated files under old ownership.
- Good, because a deleted or unreachable source cannot block uninstall.
- Bad, because missing source content means declared uninstall cleanup does not run.
- Bad, because scripts can still create external effects that rollback cannot reverse.

### Confirmation

Schema, authoring, formatter, planner, and lifecycle tests cover both uninstall
events. Real-process integration tests verify pre/post order, rollback after a
failed post-uninstall script, and successful removal with a warning when the
source is unavailable.

## Pros and Cons of the Options

### Locked Release With Checkpoint

- Good, because executed hooks match the installed release and ownership is interruption-safe.
- Bad, because post-hook failure requires restoring both files and persisted state.

### Latest Available Release

- Good, because catalog lookup is simpler.
- Bad, because uninstall could execute cleanup code the user never installed.

### Persist After Post Hooks

- Good, because successful operations write final state once.
- Bad, because interruption can leave files and ownership records inconsistent.

### Require Source Availability

- Good, because declared cleanup always runs when uninstall succeeds.
- Bad, because source loss can make managed content impossible to remove normally.

## More Information

See [ADR-0040](0040-secure-lifecycle-scripts-with-scoped-trust.md),
[ADR-0044](0044-render-lifecycle-script-arguments.md), and
[ADR-0053](0053-unify-ordered-lifecycle-hooks.md).
