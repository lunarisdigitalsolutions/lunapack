## Why

LunaPack currently identifies a requested root by pack ID, preventing one repository from installing the same reusable pack for multiple APIs, services, or modules. Instance-aware identity is needed so each installation can retain independent parameters, remaps, ownership, updates, and removal without requiring pack-author changes.

## What Changes

- Add an optional `--name <alias>` to pack install, update, and uninstall commands, and add `luna rename <pack-id> --name <alias> --to <new-alias>`.
- Treat each requested root installation as an instance identified by pack ID and alias; aliases are unique among instances of the same pack, and the pack ID is the default alias.
- Permit repeated installation of one pack only when both the alias and effective remap set distinguish the new instance from existing instances of that pack.
- Resolve parameters and variables independently for each requested-root instance while continuing to share compatible transitive dependency records by pack ID and version.
- Record instance identity, remaps, and file ownership in `lunapack-lock.yml`; preserve compatibility by interpreting an existing requested root as its pack-ID-named default instance.
- Select the sole instance or pack-ID-named default automatically for update; require `--name` when multiple instances exist without a default.
- Scope uninstall and rename operations to one instance while preserving other instances and shared reachable dependencies.
- Continue rejecting targets owned by another instance, but allow installation and update overwrite behavior for unowned existing files.
- **BREAKING** Replace dependency-cycle rejection with warnings that ignore self-references and cycle-closing edges while continuing resolution.
- **BREAKING** Existing unowned target files may be overwritten during installation or update instead of always causing preflight failure.
- Keep multi-instance support automatic for existing packs; pack manifests require no opt-in or changes.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-pack-lifecycle`: Define instance-aware install, update, uninstall, rename, ownership, remap uniqueness, independent value resolution, overwrite, and cycle-tolerant dependency behavior.
- `cli-project-configuration`: Persist portable requested-root instance aliases and per-instance placement settings while preserving existing configuration compatibility.
- `project-lockfile`: Persist requested-root instances and per-instance ownership while retaining shared transitive dependencies and migrating existing lock state.
- `manifest-schemas`: Publish and validate compatible project-configuration and lock-file representations for instance aliases, remaps, and ownership.
- `cli-workflow-guidance`: Provide actionable disambiguation and next-step guidance for instance-aware lifecycle commands.

## Impact

- CLI parsing and lifecycle orchestration in `projects/cli/src/Lunapack.Cli`, including install, update, uninstall, rename, audit, outdated reporting, dependency resolution, transaction planning, and error guidance.
- Lock domain models, serialization, migration, ownership indexes, and `projects/schema/lunapack-lock.schema.json`.
- Existing pack manifests and pack-author contracts remain compatible; project lock files gain instance identity through a compatible read-and-rewrite migration.
- Product documentation in `docs/product/pack-ecosystem-working-concept.md` and relevant lifecycle PRDs must reflect accepted instance semantics.
- Internal architecture documentation requires a new ADR plus updates to lifecycle, ownership, dependency-resolution, and lock-state guidance.
- Developer documentation for installation, updates, remapping, CLI commands, configuration, lock files, ownership, and troubleshooting must document instance selection and migration behavior only when implemented.
