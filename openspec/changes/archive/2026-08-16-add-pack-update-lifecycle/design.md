## Context

See [proposal.md](proposal.md) for motivation. The current lifecycle resolves a
complete graph, renders its files, copies only targets absent from the project,
and persists `lunapack.yml` plus `lunapack-lock.yml`. The lock file captures resolved
ownership and output digests but not an update operation or per-file strategy.
`PackInstallationPlanner` already produces rendered content and effective
project-relative targets; `PackLifecycleService` owns paired state persistence
and rollback for install and uninstall.

## Goals / Non-Goals

**Goals:**

- Plan updates by comparing the persisted resolved graph with a newly resolved
  graph before filesystem mutation.
- Apply deterministic copy and merge strategies, including JSON object/array
  semantics, while retaining transaction-style rollback.
- Keep existing manifests valid by treating a missing strategy as
  `copy/overwrite`.
- Use the same catalog and graph-resolution rules for `outdated`, named
  updates, update-all, and dry runs.

**Non-Goals:**

- Preserve user changes by rejecting an update merely because a lock-file hash
  differs; the declared update strategy controls that outcome.
- Add remote sources, version ranges, interactive install prompts, or
  three-way textual merges.
- Infer section markers or JSON merge behavior from target file names.

## Decisions

### Represent strategy in the manifest model and schema

Add an optional `strategy` object to each managed-file schema entry and model
it as a type/method pair. The JSON Schema will restrict valid method sets by
type. The parser will materialize an absent strategy as `copy/overwrite`, so
catalogs containing existing manifests remain usable without migration.

This keeps strategy beside the target mapping that it governs and prevents
invalid combinations from reaching lifecycle code. A top-level pack strategy
was rejected because one pack can require different behavior for different
files.

### Build an explicit update action plan

Introduce a lifecycle update planner that accepts prior lock state and the
newly resolved/rendered graph. It will match ownership by resolved pack and
effective target path, then emit typed actions: create, delete, copy,
backup-and-copy, line merge, section merge, and JSON merge. Each action carries
the final content used for the resulting lock digest and a human-readable
dry-run description.

The existing installation planner remains responsible for rendering and target
selection. Keeping resolution/rendering separate from reconciliation prevents
update logic from reimplementing parameters, conditions, glob expansion, or
destination behavior. A direct write-as-planning approach was rejected because
it cannot provide a trustworthy dry run or complete preflight.

### Resolve full requested-root state before an update transaction

For a named update, replace only that requested root's version constraint (or
remove its implicit constraint to select the newest version), then resolve the
entire requested-root collection. For update-all, calculate all newer eligible
root selections first; `--prompt` filters that selection before a single graph
resolution and transaction. This catches graph conflicts, target collisions,
and invalid manifests before writing a target.

The transaction will update the configuration, resolved lock graph, and all
selected target actions together. If preflight, target execution, or state save
fails, restore modified/deleted targets, delete newly created targets and
backup files, and retain the original state files. Sequential per-pack updates
were rejected because an intermediate graph can conflict with a later selected
root and leaves a partial update batch.

### Implement merge operations on parsed content

`lines` treats source and target as line sequences, preserves target order, and
appends source lines not already present. `section` treats the rendered source
first and last lines as literal inclusive markers: absent markers append the
whole source, while present markers replace the inclusive section. `json`
parses both source and target and requires their top-level kinds to match.
Object properties are recursively merged with source values replacing scalar
or kind-conflicting values; destination-only properties remain. Arrays retain
destination order and append source values not structurally equal to an
existing destination value.

Use platform-neutral UTF-8 text handling and `System.Text.Json` for JSON. This
avoids filename-specific behavior and makes malformed input or mismatched JSON
kinds a preflight failure rather than a partial update. A generic text diff was
rejected because it cannot reliably honor semantic JSON merging or stable
section ownership.

### Use catalog comparison for outdated reporting

`outdated` will load project state, identify requested roots, browse configured
sources once, and compare each currently resolved root to its highest available
candidate using the existing semantic-version comparer and source tie-breaking.
It reports no filesystem or state actions. Update commands reuse the same
candidate selector rather than parsing displayed output.

### Make dry run execute all non-mutating planning

`--dry-run` performs state loading, source catalog browsing, graph resolution,
template rendering, strategy parsing, target reads, merge calculation, and
preflight. It stops before backup, file writes/deletes, and state save. Its
output comes from planned actions, avoiding a second code path with behavior
that can drift from real execution.

## Risks / Trade-offs

- [A strategy can intentionally overwrite local changes] -> Document that
  hash drift is detected for planning/audit but does not override strategy;
  advise `--dry-run` before updates.
- [Rollback must undo mixed create, merge, delete, and backup actions] -> Take
  byte snapshots of pre-existing targets and track created files/directories
  and generated backup paths before mutation.
- [Section markers can be malformed, duplicated, or mismatched] -> Fail
  preflight on incomplete or ambiguous marker pairs; use first and last source
  lines exactly without heuristic matching.
- [JSON formatting can change after a semantic merge] -> Define lock digests
  over written output and use stable serializer options in tests; retain
  destination-only data even when formatting normalizes.
- [A new `gitignore-general` pack shares `.gitignore` with a .NET pack] -> Use
  distinct explicit section markers and verify coexistence in integration
  tests.

## Migration Plan

1. Extend schema/model/parser with the optional defaulted strategy and cover
   schema compatibility plus every invalid type/method pair.
2. Add action planning, merge executors, snapshots, and update/outdated command
   handlers; extend install with dry-run only.
3. Add focused unit tests for catalog selection, action planning, strategies,
   rollback, prompts, dry runs, and lock-state refresh; add CLI integration
   tests for named, all, and prompted updates.
4. Version and update bundled manifest examples, add `gitignore-general`, and
   refresh repository state through the new lifecycle behavior.
5. Publish product, internal, developer, and ADR documentation with the
   released implementation. Rollback releases by restoring the previous CLI
   and pack versions; a failed runtime transaction restores consumer state
   automatically.
