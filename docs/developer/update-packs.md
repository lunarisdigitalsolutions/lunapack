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

Lifecycle hooks follow the same `--scripts prompt|run|skip` modes and
`--skip-instructions` option as install. Updates process dependency-first
`preUpdate` hooks before managed-file changes and `postUpdate` hooks before
state persistence, preserving each event's declared script and instruction
order. New dependencies use install hooks. Dry runs validate and summarize
instructions without guided display. Ordinary updates are pinned to their
lock-record source identity. An explicit version available only from another
source is shown in dry-run output and requires confirmation before the update
runs.
