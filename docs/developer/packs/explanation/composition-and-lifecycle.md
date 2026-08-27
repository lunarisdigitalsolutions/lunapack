# Composition and lifecycle

Composite packs assemble exact versions of other packs. Resolution selects one
version per pack ID, detects cycles and missing references, and records the
complete graph before files change. When the same parameter occurs in a graph,
the declaration nearest the installed root controls requiredness and enum
values; every declaration must retain the type.

A composite can bind a string or Boolean parameter for a dependency. A binding
is hidden from consumers unless the root also declares that parameter. A
consumer value takes precedence over a compatible project variable; optional
unresolved strings and enums render as empty strings and optional Booleans as
false.

Install and update render selected templates, evaluate conditions, preflight
target actions, then write files and paired project state transactionally. A
dry run performs the same selection and preflight without changing files or
state. Updating recomputes the complete selected-root graph as one transaction.

Lifecycle hooks add ordered work around that transaction. Each event can mix
executable scripts and non-executable instructions in manifest order. For an
installed node, `preInstall` runs before managed files and `postInstall` runs
before state persistence; updates use `preUpdate` and `postUpdate`. Events run
dependency-first. A composite reference can list `disabledHooks`; every
incoming transient reference contributes to the event-suppression union, while
a pack installed directly as a root keeps its own hooks enabled.

LunaPack authorizes every script before processing any hook, then launches
approved scripts with literal argv and no implicit shell. Instructions load
from the operation snapshot and display without script trust. LunaPack can
restore managed files and `lunapack.yml` after failure or instruction
cancellation, but it cannot roll back external process effects. Packed content
is copied into an operation snapshot and hashed before launch. Snapshot
traversal currently follows links and reparse points; see lifecycle safety
guidance before treating a source as trusted.
