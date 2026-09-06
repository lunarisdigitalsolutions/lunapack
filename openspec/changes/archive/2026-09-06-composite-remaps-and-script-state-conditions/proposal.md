## Why

Composite pack authors cannot currently adapt a referenced pack's managed-file targets to the layout established by the consuming pack. Hook conditions also cannot react to invocation-wide script suppression or to the outcome of an earlier script, which prevents packs from offering instructions when automation did not run.

Composite parameter bindings are also limited to literals. Authors cannot pass a
parameter through under a different name or derive a Boolean or choice from the
same expression grammar used by conditions.

## What Changes

- Allow composite references in `pack.yml` to declare file and directory remaps for the referenced pack and its dependency subtree.
- Compose nested composite remaps with nearest-reference precedence while preserving consumer authority: command-line, requested-root, and top-level project remaps override pack-authored composite remaps.
- Add `scriptsSkipped()` to lifecycle-hook conditions. It returns true when scripts are suppressed for the whole invocation by `--scripts skip`, equivalent configured behavior, or trust-policy denial; individual conditions and disabled hooks do not affect it.
- Add `previousScriptState()` to lifecycle-hook conditions. It returns `succeeded`, `failed`, `skipped`, `cancelled`, `ignored`, or `none` for the nearest earlier script declaration in the same pack and lifecycle event.
- Preserve current abort and rollback behavior after script failure or cancellation; later hooks do not run in those cases even though those states remain part of the condition contract.
- Allow a referenced pack parameter value to use an exact `${{ expression }}`
 binding. A parameter identifier passes through its typed value, while
 `iif(condition, whenTrue, whenFalse)` selects a typed value with the shared
 condition expression grammar.
- Extend pack-authoring commands, validation, dry-run output, tests, and public documentation for the new declarations and functions.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `manifest-schemas`: Permit remaps and typed expression bindings on composite
 references and recognize lifecycle runtime-state functions in hook conditions.
- `local-pack-lifecycle`: Resolve cascading composite remaps and transient
 parameter expressions with explicit precedence, and evaluate runtime-aware
 hook conditions in declaration order.
- `pack-authoring`: Let authors create, inspect, replace, and validate composite
 remaps, expression bindings, and runtime-state hook conditions.

## Impact

The change affects the public `pack.yml` schema, manifest model and validation,
parameter and composite graph resolution, managed-file planning, lifecycle hook
planning and dispatch, authoring commands, dry-run and inspection output, and
focused unit and integration coverage. Public developer documentation must
explain expression binding syntax, composite remap scope, precedence, and
condition return values. Internal parameter, path-handling, and lifecycle
documentation must record evaluation timing, and ADRs must capture the durable
expression, precedence, and runtime-state decisions. No new dependency is
expected.
