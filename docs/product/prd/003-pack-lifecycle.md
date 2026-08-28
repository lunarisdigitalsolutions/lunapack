# Pack Lifecycle

## Current Scope

Packs can be file-only, composite-only, or mixed. Composite references use
exact versions; source selection remains consumer-owned. The resolver builds
one complete graph before file or state changes.

Managed files support source, directory, and glob selectors; typed parameters;
restricted conditions; templates; and copy or merge strategies. The lock
document records resolved sources, dependencies, effective targets, and
rendered-content digests.

Pack authors can declare credential-free external Git sources under local
aliases and select files, directories, or globs from them. Consumers retain
authority: equivalent workspace sources are reused, missing sources require one
graph-wide approval, and source additions, files, and provenance commit in one
transaction. Updates compare selected paths and hashes at current symbolic refs;
audit reports each external file's pack ownership and source mapping.

Consumers can map portable declared managed-file targets to repository-specific
directories or exact files through project configuration, `luna remap
set <directory|file> <target> <newTarget>`, or one installation command.
`luna remap list` shows configured mappings and `luna remap rm` removes one.
The lock records each declared and effective target pair. Updates and uninstalls
retain the recorded effective target; changing configuration never relocates
existing files implicitly. Consumers use `luna mv <source> <target>` to move one
uniquely owned file, including rebinding ownership after a manual move.

Managed-file templates can resolve a selected file by its declared target and
render either its effective project-relative target or a path relative to the
current template's effective directory. Resolution uses the complete planned
target set, returns slash-only paths, and exposes no filesystem access. Missing,
conditionally excluded, or ambiguous references warn and preserve the declared
target. Install, update, and dry-run share this behavior.

Installation and update preflight the full plan; dry runs do not change project
files or state. Transactions restore files when a write or state save fails.
Uninstall preserves changed or still-owned targets.

Optional `preInstall`, `postInstall`, `preUpdate`, and `postUpdate` arrays contain
ordered script and instruction hooks. Scripts support automation that cannot be
represented as managed files. They are not sandboxed; consumers choose prompt,
run, or skip behavior and own persisted trust by source or by pack and source.
Instructions display pack-authored Markdown without executing it and can be
skipped independently. Composite packs suppress every typed hook in a selected
dependency event.

Lifecycle planning preserves dependency-first event order and manifest
declaration order. Every script is authorized before any hook is processed.
Instruction files load from the operation snapshot as strict UTF-8 and may use
explicit Scriban rendering. Dry runs validate and summarize both types without
execution or guided display. Pre-hooks run before managed-file mutation;
post-hooks run before state persistence. Failures and interactive cancellation
restore LunaPack-managed files and state, but cannot reverse external script
effects.
