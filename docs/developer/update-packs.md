# Update packs

Check whether an installed root pack has a newer release, then preview the
update before applying it.

```powershell
luna outdated
luna update dotnet-project --dry-run
luna update dotnet-project
```

Run `luna update --dry-run` to plan updates for every installed root. Use
`luna update --prompt` when selecting which available updates to apply.

LunaPack recomputes the selected-root graph and updates project files and lock
state together. Modified managed files stay in the project for review.

Lifecycle hooks follow the same `--scripts prompt|run|skip` modes as install.
Updates run dependency-first `preUpdate` hooks before managed-file changes and
`postUpdate` hooks before state persistence. New dependencies use install
hooks. Ordinary updates are pinned to their lock-record source identity. An
explicit version available only from another source is shown in dry-run output
and requires confirmation before the update runs.
