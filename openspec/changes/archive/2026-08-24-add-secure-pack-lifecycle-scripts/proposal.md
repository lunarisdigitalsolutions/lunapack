## Why

Packs currently distribute declarative files but cannot perform explicit setup or migration work. Lifecycle hooks are needed, but arbitrary pack commands require visible, deliberate consent and narrowly defined trust so automation does not silently expand the execution boundary.

## What Changes

- Add one optional script declaration for each `pre-install`, `post-install`, `pre-update`, and `post-update` hook in `pack.yml`. A declaration selects either a confined file shipped in the pack with an explicit runner or a direct external command, plus ordered arguments and an optional description.
- Execute hooks for directly requested and transient packs whenever those packs are installed or updated. Allow each composite reference to suppress selected lifecycle hook types for its referenced pack; when a shared transient pack has multiple incoming policies, the most restrictive policy wins.
- Execute hook commands directly without a shell, reject symbolic links and reparse points during snapshot traversal, confine pack-relative files to an immutable private snapshot, and validate the complete hook plan before project mutation.
- Require per-hook confirmation by default. Show the pack ID, hook, optional description, and exact command before consent.
- Add `--scripts <prompt|run|skip>` to `luna install` and `luna update`. `prompt` is the default, `run` executes every non-suppressed hook without confirmation, and `skip` executes no hooks.
- Add `luna trust source <name>...` and `luna trust pack <id>...` with local-user, project, and global-user scopes. Local-user trust is the default, project trust is declared in `lunapack.yml`, and local/global user trust is stored cross-platform under `~/.lunapack/config.yml`.
- Bind pack trust to source identity plus bare pack ID. Require an explicit danger confirmation before every trust mutation, including warnings about repository or source compromise, future pack versions, inherited user permissions, credentials, network access, and irreversible side effects.
- Require every newly added local, Git, or GitHub source to have a unique name, persist the name, and include it in `luna sources list` output.
- Record immutable configured-source identity for every root and transient pack in `lunapack-lock.yml`. Keep ordinary updates on the locked source; allow explicit `pack-id@version` to switch source only after showing and confirming the old and new source identities.
- Verify the exact bytes and existence of `lunapack.yml` after every hook. Restore it, abort immediately, and roll back LunaPack-owned state if a hook changes or removes it.
- Extend `luna inspect` with a lifecycle-script table showing which hooks run and their exact commands.
- Extend the existing version-1 YAML schemas and update checked-in manifests, examples, fixtures, and documentation without introducing a new schema version.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `manifest-schemas`: Define lifecycle-script declarations, per-reference hook suppression, named sources, scoped trust, and locked source identity in the existing manifest versions.
- `cli-project-configuration`: Require names when adding sources, list source names, and manage warning-gated local-user, project, and global-user trust.
- `local-pack-lifecycle`: Plan, suppress, authorize, source-pin, order, execute, integrity-check, and report direct and transient install/update hooks securely.
- `pack-catalog`: Show lifecycle hooks, composite suppression, descriptions, and exact commands during pack inspection.

## Impact

- Affects pack, project, lock, and user-settings models; YAML serialization; JSON Schemas; source catalog provenance; install/update planning; process execution; prompting; trust evaluation; inspect/list formatting; and CLI composition.
- Requires focused unit and integration coverage for schema validation, no-follow snapshotting, shell-free invocation, consent and non-interactive denial, trust scope and acknowledgement, source pinning and switching, transient suppression, hook ordering/failure, project-manifest integrity, named source commands, and inspection output.
- Requires developer documentation for pack authors and consumers, internal security and lifecycle documentation, an accepted ADR for executable-content trust and lifecycle ordering, and updates to checked-in version-1 YAML examples and pack manifests.
- Adds no runtime package dependency and does not change the declared project or lock schema version.
