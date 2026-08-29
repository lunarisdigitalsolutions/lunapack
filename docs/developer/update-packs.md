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

LunaPack recomputes the selected-root graph and updates project files and lock
state together. Existing target behavior follows each managed-file strategy:
copy strategies may replace, back up, retain, or reject current content, while
merge strategies combine it according to their method. Review the dry run
instead of assuming local edits are preserved. Symbolic external refs are
refreshed even when the pack version is unchanged. Only changed selected paths
or hashes produce an update; unrelated upstream commits do not. Removed
external requirements release lock consumers but leave workspace sources
configured. `--offline` avoids remote ref checks and reports that freshness
could not be confirmed from remote state.

Ordinary updates are pinned to their lock-record source identity. An explicit
version available only from another source is shown in dry-run output and
requires confirmation before the update runs.

Packs may run scripts or show instructions during updates. See
[Lifecycle hooks](lifecycle-hooks.md) before changing hook behavior or trust.
Persistent denial cannot be bypassed with `--scripts run`; updates warn before
mutation and continue without denied scripts.

Use [Audit ownership and recover drift](advanced/audit-and-recover.md) when
source configuration or installed targets no longer match expected state.
