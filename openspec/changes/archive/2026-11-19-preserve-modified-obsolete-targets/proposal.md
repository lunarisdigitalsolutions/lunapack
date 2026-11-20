# Proposal

## Why

Pack updates currently delete obsolete targets even when their bytes differ from
the ownership digest in `lunapack-lock.yml`. This can destroy local edits, so
updates need the same digest-based protection users already expect from other
lifecycle operations.

## What Changes

- Delete obsolete managed targets by default only when their current digest
  matches the previous lock-file digest.
- Preserve locally modified obsolete targets, warn, show the preservation in
  dry-run output, and remove their stale lock ownership.
- Add `luna update --remove-modified` as an explicit opt-in to delete modified
  obsolete targets.
- Recheck target content before transaction deletion and handle shared
  merge-strategy targets once per effective path.
- Update public update guidance, CLI reference, changelog, and internal
  lifecycle decision records.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-pack-lifecycle`: Protect modified targets no longer owned by the
  resolved update graph, with a scoped removal override and dry-run reporting.
- `luna-links`: Preserve modified targets removed from a link's next selection
  and require delete actions to retain a planning-time digest.

## Impact

- CLI pack-update planning, transaction, command options, warnings, and dry-run
  formatting.
- Unit tests for planning, transaction safety, command parsing, output, and
  lifecycle state.
- `docs/developer/update-packs.md`, `docs/developer/cli/commands.md`,
  `projects/cli/CHANGELOG.md`, and a new architecture decision record.
- No lock-file schema change; retained obsolete files lose ownership in the
  next successful lock state.
