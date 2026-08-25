## Why

Installed packs are currently immutable after installation. Consumers need a
safe, inspectable way to move an installed pack to a selected or newest release
while applying additions, removals, and source changes according to each
managed file's declared update behavior.

## What Changes

- Add `lunapack update [<pack-id>@<version>]` for a selected installed root pack
  or all outdated installed root packs. A versionless update resolves the
  highest semantic version from all configured sources; an explicit version
  must be available from those sources.
- Add `lunapack outdated` to report installed root packs for which a newer
  configured-source version is available, including current and latest
  versions.
- Add `--prompt` for per-pack confirmation during update-all and `--dry-run`
  to install and update commands. Dry runs report planned actions without
  modifying project files or state.
- Extend `pack.yml` managed files with an optional update strategy, defaulting
  to `copy/overwrite`: `copy` accepts `overwrite`, `fail-if-exists`,
  `skip-if-exists`, or `backup-and-overwrite`; `merge` accepts `lines`,
  `section`, or `json` methods.
- Apply strategies when updating new, changed, and removed managed files;
  preserve update atomicity and refresh resolved versions, managed-file hashes,
  and graph state after a successful update. Strategies apply even when the
  target hash differs from the lock-file hash.
- Publish `gitignore-general` and change the `dotnet-gitignore` pack to use
  section merging. Change `dotnet-sdk-10` and `dotnet-csharpier-tool` to JSON
  merging. Declare the `license-mit` target as copy/overwrite.
- Document the update workflow, merge behavior, dry-run semantics, manifest
  strategies, and the durable update-state design.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-pack-lifecycle`: Update installed packs, expose outdated checks and
  dry-run behavior, and apply strategy-driven managed-file synchronization.
- `bundled-engineering-packs`: Publish merge-strategy examples and the generic
  gitignore pack alongside strategy-aware existing packs.

## Impact

- Affected CLI code: command registration, lifecycle planning/execution,
  catalog resolution, manifest/state models, and filesystem transaction logic.
- Affected contracts: `pack.yml`, `lunapack.yml`, `lunapack-lock.yml`, and CLI
  command/option behavior.
- Affected packs: `dotnet-gitignore`, new `gitignore-general`,
  `dotnet-sdk-10`, `dotnet-csharpier-tool`, and `license-mit`.
- Affected documentation: product pack lifecycle requirements; internal
  architecture and ADR records; developer command and pack-author references.
