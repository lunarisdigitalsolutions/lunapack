## Why

Pack manifests provide portable default targets, but consumers cannot preserve a
different repository layout across installation, updates, inspection, and
uninstallation. Consumers also need a supported way to relocate an already
managed file without losing its ownership record.

## What Changes

- Add project-level directory and file remapping rules in `lunapack.yml` that
  redirect manifest-declared managed-file targets.
- Add `luna remap set <directory|file> <target> <newTarget>` to upsert reusable
  global remapping policy without directly editing `lunapack.yml`, with `list`
  and `rm` subcommands for inspection and removal.
- Add repeatable `luna install` remapping options for directory and individual
  file targets, with resolved locations persisted as managed-file lock state.
- Resolve and persist each managed file's effective target path in
  `lunapack-lock.yml` so update and uninstall operate on its remapped location.
- Add `luna mv <source> <target>` to relocate an installed managed file when
  present, or rebind existing ownership when the destination already exists.
- Expand `luna inspect` to display each managed manifest target and its
  effective remapped target when project remapping applies.
- Document the configuration and CLI behavior for pack consumers, update the
  configuration schema and validation tests, and record the durable lock-file
  contract decision in an ADR.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-pack-lifecycle`: Installation, update, inspection, and uninstallation
  resolve remapped managed-file targets and support relocation of managed files.
- `project-lockfile`: Lock state records remapped managed-file ownership so
  lifecycle commands operate on the effective target.
- `cli-project-configuration`: Project configuration declares validated,
  persistent directory and file remapping rules.
- `pack-catalog`: Pack inspection presents managed-file targets and any
  effective project remapping.

## Impact

Affected areas include the `luna remap`, `luna install`, `luna inspect`,
`luna update`, `luna uninstall`, and new `luna mv` command paths; project configuration and
lock-file models, YAML schemas, CLI unit and integration tests, developer pack
installation/reference documentation, internal architecture documentation, and
product lifecycle documentation.
