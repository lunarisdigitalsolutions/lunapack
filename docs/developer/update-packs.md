# Update packs

Check whether an installed root pack has a newer release, then preview the
update before applying it.

After applying one or more updates, Luna recommends auditing resolved state and
checking again for outdated roots. A dry run shows only planned changes.

```powershell
luna outdated
luna update dotnet-project --dry-run
luna update dotnet-project
```

Run `luna update --dry-run` to plan updates for every installed root. Use
`luna update --prompt` when selecting which available updates to apply.

LunaPack recomputes the selected-root graph and updates project files and lock
state together. Modified managed files stay in the project for review.

Ordinary updates are pinned to their lock-record source identity. An explicit
version available only from another source is shown in dry-run output and
requires confirmation before the update runs.

Packs may run scripts or show instructions during updates. See
[Lifecycle hooks](lifecycle-hooks.md) before changing hook behavior or trust.
