## Why

Successful Luna commands report what happened but leave users to infer the next
workflow step. Contextual guidance should make the core journey discoverable
from the CLI while avoiding irrelevant command lists.

## What Changes

- Make the root `luna` command summarize workspace maturity and recommend the
  next meaningful workflow actions.
- Introduce one internal advisor and one rendering contract for workspace-aware,
  command-specific recommendations.
- Add concise guidance after successful initialization, source management,
  catalog, installation, update, and uninstallation commands.
- Add actionable recovery commands for missing workspaces, missing sources, and
  unresolved pack references.
- Add `luna sources rm <name>` so source-removal guidance can reflect whether
  other sources remain.
- Limit every guidance block to three ordered actions with command input that is
  either directly runnable or clearly marked for replacement.

## Capabilities

### New Capabilities

- `cli-workflow-guidance`: Classify workspace maturity and render contextual
  next actions for the root command, successful core workflows, and recoverable
  errors.

### Modified Capabilities

- `cli-project-configuration`: Guide users after initialization and source
  changes, and support removing a configured source by name.
- `pack-catalog`: Guide users after discovery, search, and inspection, and from
  catalog failures caused by missing sources or unresolved packs.
- `local-pack-lifecycle`: Guide users after installation, update, and
  uninstallation, and from installation failures caused by unresolved packs.

## Impact

- Affected code: root command dispatch, project initialization, source command
  handlers, catalog handlers, lifecycle handlers, workspace-state inspection,
  and shared console rendering.
- Affected tests: root invocation, maturity classification, recommendation
  ordering and limits, command-specific success output, source removal, and
  relevant error output.
- Affected product documentation: CLI and MVP requirements describing the
  guided core workflow.
- Affected internal documentation: CLI composition, workspace-state ownership,
  and guidance-rendering boundaries; implementation will add an ADR for the
  durable advisor boundary.
- Affected developer documentation: CLI overview, command reference, source
  management, pack installation, and pack update guidance.
- No configuration, lock-file, pack-manifest, or dependency contract changes.
