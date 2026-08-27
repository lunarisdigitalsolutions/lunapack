---
status: superseded by ADR-0055
date: 2026-08-27
decision-makers: LunaPack maintainers
---

# ADR-0053: Unify Ordered Lifecycle Hooks

## Context and Problem Statement

Pack manifests previously allowed one script declaration per lifecycle event.
That shape could not interleave multiple executable scripts with manual setup
instructions or preserve their declared order. LunaPack needs one lifecycle
model without extending script trust to non-executable content.

## Decision Drivers

- Preserve declaration order across scripts and instructions.
- Authorize every executable script before processing any lifecycle hook.
- Keep instruction loading confined to the materialized operation snapshot.
- Preserve transactional rollback around pre- and post-mutation hooks.
- Support predictable interactive and automated behavior.
- Keep Markdown parsing bounded to required step semantics.

## Considered Options

- Use ordered, discriminated hook arrays per lifecycle event.
- Keep separate script and instruction maps.
- Assign generated IDs to every hook declaration.

## Decision Outcome

Chosen option: "Use ordered, discriminated hook arrays per lifecycle event",
because one sequence preserves author intent while each `type` retains distinct
validation, authorization, and dispatch behavior.

The `hooks` manifest property contains arrays for `preInstall`, `postInstall`,
`preUpdate`, and `postUpdate`. Each item is either `type: script` or
`type: instruction`. The former top-level `scripts` property is rejected rather
than migrated implicitly because accepting both shapes would make ordering and
compatibility ambiguous.

LunaPack prepares applicable hooks from the copied operation snapshot before
mutation. Script arguments render before dry-run output and authorization.
Instruction files use strict UTF-8, may opt into Scriban rendering, and are
parsed into an introduction plus steps headed by `##` or `###`. Other Markdown
constructs receive no special behavior.

All planned scripts are authorized before any hook is dispatched. Instructions
do not execute and do not use script trust. `--scripts skip` removes scripts
only; `--skip-instructions` prevents instruction files from being loaded,
rendered, parsed, or displayed.

Prepared hooks retain dependency-first event order and declaration order.
Pre-mutation hooks run before managed-file changes. Post-mutation hooks run
before state persistence. Failure or interactive cancellation uses the existing
transaction rollback boundary, although external script effects remain
irreversible. Interactive instructions pause after each step; noninteractive
instructions print completely without reading input. Dry runs validate and
describe hooks without executing scripts or entering guided display.

### Consequences

- Good, because mixed lifecycle work has one stable ordering model.
- Good, because instruction display cannot acquire process-execution authority.
- Good, because skip controls remain independent and automation never waits for
  instruction input.
- Good, because position-based authoring avoids adding runtime-only manifest IDs.
- Bad, because manifests and authoring commands using `scripts` require a
  breaking migration.
- Bad, because one-based positions shift when an earlier declaration is removed.
- Bad, because the bounded parser is not a general Markdown renderer.

### Confirmation

Schema and model tests reject legacy declarations and preserve mixed order.
Planner, authorization, lifecycle, dry-run, authoring, formatter, and real CLI
integration tests cover both hook types, suppression, skip controls, rollback,
interactive behavior, and preparation failures.

## Pros and Cons of the Options

### Ordered Discriminated Hook Arrays

- Good, because order and type-specific contracts are explicit in YAML.
- Bad, because existing script manifests must change shape.

### Separate Script and Instruction Maps

- Good, because old script declarations could remain readable.
- Bad, because no canonical mixed order exists between maps.

### Generated Hook IDs

- Good, because mutation selectors would remain stable after removals.
- Bad, because IDs add schema and authoring overhead without runtime meaning.

## More Information

See [ADR-0040](0040-secure-lifecycle-scripts-with-scoped-trust.md),
[ADR-0044](0044-render-lifecycle-script-arguments.md), and
[ADR-0052](0052-inherit-terminal-for-interactive-lifecycle-hooks.md).
