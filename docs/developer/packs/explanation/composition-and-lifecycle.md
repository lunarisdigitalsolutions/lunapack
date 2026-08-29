# Composition and lifecycle

## Resolve one ordered graph

Composite packs assemble exact versions of other packs. Resolution selects one
version per pack ID, detects cycles and missing references, and records the
complete graph before files change. Two roots can share a dependency only when
they resolve it to the same exact version. A version conflict stops resolution
before files or project state change.

A shared dependency is one graph node. Luna resolves, locks, writes, and
processes its lifecycle hooks once per graph, not once per incoming reference.
Resolution visits requested roots in command or configuration order and walks
each manifest's `packs` list depth-first. Dependencies precede their consumers;
siblings retain declaration order; and a shared node keeps the position of its
first visit. This order also controls cross-pack hooks and managed-file merge
contributions. There is no separate consumer ordering override.

## Resolve graph-wide parameters

Parameter names form one namespace across the graph. Every pack declaring the
same name receives the same resolved value; consumers cannot assign a different
value to each pack. Declarations must agree on type and on scalar versus
multi-select shape.

The declaration nearest an installed root wins as a complete declaration,
including requiredness, enum values, default, display name, and description.
These fields are not merged. A root declaration beats dependency declarations.
For equal-depth ties, later requested roots and later-resolved sibling
dependencies win. Reordering roots or references can therefore change the
winning declaration.

A composite can bind a string or Boolean parameter for a dependency. A binding
is hidden from consumers unless a root declares that parameter. A hidden
binding is fixed across the graph: explicit `--parameter` input and
`--skip-variable` cannot override it. If several references bind the same
hidden name, the first binding encountered while evaluating roots from last to
first wins. Avoid competing bindings; they are not scoped to one dependency.

For an exposed parameter, an explicit consumer value takes precedence over a
compatible project variable, then the winning declaration's default. Optional
unresolved strings and scalar enums render as empty strings, optional
multi-select enums render as empty arrays, and optional Booleans render as
false.

## Apply files and lifecycle events

Install and update render selected templates, evaluate conditions, preflight
target actions, then write files and paired project state transactionally. A
dry run performs the same selection and preflight without changing files or
state. Updating recomputes the complete selected-root graph as one transaction.

Lifecycle hooks add ordered work around that transaction. Each event can mix
executable scripts and non-executable instructions in manifest order. For an
installed node, `preInstall` runs before managed files and `postInstall` runs
after mutation. During update, each new node uses install hooks, each
version-changed node uses update hooks, and each unchanged node runs no hooks.
A dependency removed by an update does not run uninstall hooks. Explicit
uninstall uses `preUninstall` and `postUninstall` for removed nodes.

Events follow resolved graph order and preserve declaration order within each
pack. A composite reference can list `disabledHooks`; every incoming transient
reference contributes to the event-suppression union, while a pack installed
directly as a root keeps its own hooks enabled.

LunaPack authorizes every script before processing any hook, then launches
approved scripts with literal argv and no implicit shell. Instructions load
from the operation snapshot and display without script trust. LunaPack can
restore managed files and `lunapack.yml` after failure or instruction
cancellation, but it cannot roll back external process effects. Packed content
is copied into an operation snapshot and hashed before launch. Snapshot
traversal currently follows links and reparse points; see lifecycle safety
guidance before treating a source as trusted.

LunaPack checkpoints configuration and lock ownership after managed-file
mutation and before post hooks. A handled post-hook failure restores the prior
checkpoint and files; a hard interruption leaves ownership matching the applied
mutation. Uninstall uses exact locked releases for hooks and continues without
them, with a warning, when source content cannot be materialized.
