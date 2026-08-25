---
status: accepted
date: 2026-08-16
decision-makers: LunaPack maintainers
---

# ADR-0018: Plan Strategy-Aware Pack Updates Before Mutation

## Context and Problem Statement

Installed packs need to move to an explicit or latest configured release
without leaving partially updated targets, backups, or state documents. Update
behavior also varies by managed file: a target can be replaced, preserved,
backed up, or merged as lines, a marked section, or JSON. A second pack can
legitimately manage a separate marked section of the same target.

The existing resolved lock graph records exact releases and rendered ownership
digests, but it did not define a reconciliation plan, preview, or rollback
boundary for a new graph.

## Decision Drivers

- Resolve all selected requested roots before writing any target.
- Make dry-run output identical in meaning to a real update plan.
- Preserve target and paired-state atomicity across mixed action kinds.
- Keep source and semantic-version precedence in one candidate-selection path.
- Permit only safe, explicit shared-target ownership.

## Considered Options

- Update roots sequentially and write files during resolution.
- Add command-specific update and dry-run code paths.
- Plan typed actions for the complete graph, then transact once.

## Decision Outcome

Chosen option: "Plan typed actions for the complete graph, then transact once",
because it makes preflight and preview trustworthy while preserving rollback
for targets, backups, configuration, and lock state.

`outdated` and `update` select configured-source releases through the catalog.
The lifecycle resolves the complete next graph, renders files, and creates
typed actions before filesystem mutation. `--dry-run` consumes that plan only;
real execution applies it through a transaction and saves both state documents
only after every target action succeeds.

Managed-file strategies are explicit `copy` or `merge` type/method pairs. An
omitted strategy remains `copy/overwrite`. Shared target ownership is allowed
only when all owners use `merge`; merge actions are chained in deterministic
plan order and every owner records the final target digest.

### Consequences

- Good, because graph conflicts, malformed merge inputs, and unsupported
  strategies fail before target or state mutation.
- Good, because prompt filtering still commits accepted roots as one batch.
- Good, because previews and real updates use one planning path.
- Good, because marked-section packs can coexist on one target.
- Bad, because update planning reads every relevant target and renders the full
  resolved graph before execution.
- Bad, because shared target ownership requires careful strategy declarations
  and final-digest handling.

### Confirmation

Schema, planner, transaction, lifecycle, command, and process integration tests
cover strategy validation, explicit/latest selection, prompts, dry-run
invariants, preflight failure, rollback, and coexistence of two marked
`.gitignore` sections.

## Pros and Cons of the Options

### Update roots sequentially and write files during resolution

- Good, because the initial implementation is smaller.
- Bad, because later graph conflicts or save failures can leave a partial batch.
- Bad, because it cannot reliably show a complete dry-run result.

### Add command-specific update and dry-run code paths

- Good, because each command can appear locally simple.
- Bad, because preview behavior can drift from real mutation behavior.
- Bad, because merge and rollback rules would be duplicated.

### Plan typed actions for the complete graph, then transact once

- Good, because planning is inspectable, reusable, and non-mutating.
- Good, because one rollback boundary covers files, backups, and paired state.
- Bad, because lifecycle code owns more explicit action and snapshot types.

## More Information

This decision extends [ADR-0005](0005-protect-local-managed-files.md),
[ADR-0014](0014-adopt-source-dispatched-pack-catalog.md),
[ADR-0016](0016-split-portable-configuration-from-resolved-lock-state.md), and
[ADR-0017](0017-bind-pack-parameters-before-rendered-ownership.md). Acceptance
criteria and implementation evidence are in the
[pack update lifecycle OpenSpec change](../../../../openspec/changes/archive/2026-08-16-add-pack-update-lifecycle/design.md).
