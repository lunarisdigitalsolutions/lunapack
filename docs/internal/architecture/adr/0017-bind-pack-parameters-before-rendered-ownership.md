---
status: accepted
date: 2026-08-16
decision-makers: LunaPack maintainers
---

# ADR-0017: Bind Pack Parameters Before Rendered Ownership

## Context and Problem Statement

Parameterized pack templates introduce input contracts and generated content
into the local lifecycle. Resolving inputs per composite node could produce
late partial failures or conflicting interpretations of a shared name.
Comparing and hashing source templates would likewise make adoption and
ownership describe bytes that the consumer did not receive.

The lifecycle must retain existing target-conflict checks, transactional
rollback, and protections for user-modified managed files. Parameter values
must remain transient because explicit CLI values may be sensitive.

## Decision Drivers

- Validate every pack parameter before a lifecycle mutation.
- Let one compatible input serve every node in a composite graph.
- Keep adoption and ownership tied to the exact installed bytes.
- Restrict template and condition execution to the declared contract.
- Preserve portable configuration and resolved-lock-state boundaries.

## Considered Options

- Resolve parameters independently while each graph node is planned.
- Persist parameter values and retain source-template ownership digests.
- Bind the complete graph once and use rendered bytes for ownership.

## Decision Outcome

Chosen option: "Bind the complete graph once and use rendered bytes for
ownership", because it validates a single deterministic contract before
mutation and records exactly the bytes materialized in the consumer project.

After graph resolution, LunaPack merges same-name declarations only when their
type, requiredness, and enum values agree. It binds values in this order:
explicit `--parameter`/`-p` input, eligible project variables, then typed empty
values for optional parameters. `--no-variables` and `--skip-variable` remove
only the project-variable source. Resolved values are passed to planning but
are not persisted in `lunapack.yml` or `lunapack-lock.yml`.

The planner validates a restricted condition language, evaluates conditions
before selector expansion, and renders each selected template as strict UTF-8
Scriban content. It uses rendered bytes for adoption checks, target writes, and
SHA-256 lock digests. Template, UTF-8, condition, or parameter failures end
planning before target or state mutation.

### Consequences

- Good, because a shared composite parameter has one visible, deterministic value.
- Good, because adoption, rollback, and uninstall protection operate on installed bytes.
- Good, because explicit values are not retained in project state.
- Bad, because existing template content must escape literal Scriban delimiters.
- Bad, because time-dependent templates produce a new digest when rendered at a different time.
- Bad, because parameter aggregation and rendering add preflight work to installation.

### Confirmation

Schema, unit, planner, lifecycle, and process integration tests validate typed
values, shared composite parameters, variable precedence and skips, conditions,
render failures, rendered adoption, generated lock digests, and rollback.

## Pros and Cons of the Options

### Resolve parameters independently while each graph node is planned

- Good, because each pack could own its parameter processing locally.
- Bad, because shared declarations could fail after other graph work began.
- Bad, because users would supply the same composite input multiple times.

### Persist parameter values and retain source-template ownership digests

- Good, because later operations could inspect prior explicit input directly.
- Bad, because explicit values may be sensitive and do not identify installed bytes.
- Bad, because adoption could claim a target that differs from its template source.

### Bind the complete graph once and use rendered bytes for ownership

- Good, because the resolved contract is validated before planning and mutation.
- Good, because lock ownership and installed content remain equivalent.
- Bad, because templates must be rendered before target comparison and digesting.

## More Information

This decision extends [ADR-0005](0005-protect-local-managed-files.md) and
aligns with [ADR-0016](0016-split-portable-configuration-from-resolved-lock-state.md).
Implementation and acceptance criteria are in the
[pack template parameters OpenSpec change](../../../../openspec/changes/archive/2026-08-16-add-pack-template-parameters/design.md).
