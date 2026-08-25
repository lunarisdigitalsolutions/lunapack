# Pack Lifecycle

## Current Scope

Packs can be file-only, composite-only, or mixed. Composite references use
exact versions; source selection remains consumer-owned. The resolver builds
one complete graph before file or state changes.

Managed files support source, directory, and glob selectors; typed parameters;
restricted conditions; templates; and copy or merge strategies. The lock
document records resolved sources, dependencies, effective targets, and
rendered-content digests.

Consumers can map portable declared managed-file targets to repository-specific
directories or exact files through project configuration, `luna remap
set <directory|file> <target> <newTarget>`, or one installation command.
`luna remap list` shows configured mappings and `luna remap rm` removes one.
The lock records each declared and effective target pair. Updates and uninstalls
retain the recorded effective target; changing configuration never relocates
existing files implicitly. Consumers use `luna mv <source> <target>` to move one
uniquely owned file, including rebinding ownership after a manual move.

Installation and update preflight the full plan; dry runs do not change project
files or state. Transactions restore files when a write or state save fails.
Uninstall preserves changed or still-owned targets.

Optional `preInstall`, `postInstall`, `preUpdate`, and `postUpdate` hooks support
automation that cannot be represented as managed files. Hooks are not sandboxed.
Consumers choose prompt, run, or skip behavior and own persisted trust by source
or by pack and source. Composite packs can suppress selected dependency hooks.
