# Update packs

Check whether an installed root pack has a newer release, then preview the
update before applying it.

After applying one or more updates, Luna recommends auditing resolved state and
checking again for outdated roots. A dry run shows only planned changes.

```powershell
luna outdated
luna outdated --offline
luna update dotnet-project --dry-run
luna update dotnet-project
```

Run `luna update --dry-run` to plan updates for every installed root. Use
`luna update --prompt` when selecting which available updates to apply.
Dry runs ask for every configurable pack parameter before planning. Pass
`--prompt-parameters` on a real update to answer optional parameters as well;
each prompt offers its declared default.

Dry runs group source changes, managed-file actions, and lifecycle actions
under labeled sections with ASCII action markers. A successful update lists
managed-file changes by default. Pass `--no-file-change-output` to suppress
that success list without hiding the dry-run plan. When the selected pack ID
and version exist in multiple configured sources, both forms identify the
chosen source by name and type.

## Understand version intent

An update without an explicit version selects the latest available release and
makes that root float for future updates by removing its requested `version`
from `lunapack.yml`. This also happens to roots selected for a newer release by
update-all or `--prompt`. Roots with no available version change keep their
existing request.

Use an exact reference when the project should remain pinned:

```powershell
luna update dotnet-project@1.0.0
```

The explicit version is persisted in `lunapack.yml`. Both forms still record
the exact resolved release in `lunapack-lock.yml`.

## Understand file decisions

LunaPack recomputes the selected-root graph and updates project files and lock
state together. For an already owned target, Luna first compares newly rendered
pack content with the previous locked digest. When they match, update plans no
file action even if the current target was edited locally. The edit remains
visible through `luna audit`; update does not provide a force-reconcile option.

When desired pack content changes, the declared strategy applies to current
target content. Copy strategies may replace, back up, retain, or reject it,
while merge strategies combine it according to their method. Review the dry
run instead of assuming local edits are preserved.

When a new pack version stops declaring an owned target, update deletes that
target without comparing its current bytes with the locked digest. This differs
from uninstall, which refuses to remove a modified target. Use `--dry-run` to
review deletions. A project `@ignore` remapping preserves a target while
dropping ownership when that is the intended migration.

`backup-and-overwrite` moves the current target to the first unused sibling
name `<target>.1`, `<target>.2`, and so on, then writes new content. Existing
number gaps are reused. Backups are not managed in the lock file and Luna does
not expire or remove them.

Symbolic external refs are refreshed even when the pack version is unchanged.
Only changed selected paths or hashes produce an update; unrelated upstream
commits do not. Removed external requirements release lock consumers but leave
workspace sources configured. `--offline` avoids remote ref checks and reports
that freshness could not be confirmed from remote state.

Ordinary updates are pinned to their lock-record source identity. An explicit
version available only from another source is shown in dry-run output and
requires confirmation before the update runs.

Packs may run scripts or show instructions during updates. See
[Lifecycle hooks](lifecycle-hooks.md) before changing hook behavior or trust.
Persistent denial cannot be bypassed with `--scripts run`; updates warn before
mutation and continue without denied scripts.

Use [Audit ownership and recover drift](advanced/audit-and-recover.md) when
source configuration or installed targets no longer match expected state.
