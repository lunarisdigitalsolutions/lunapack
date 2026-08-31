# Discover and install packs

After adding a source, list available packs and preview the change before
writing files.

Discovery recommends an install command when packs are available. After a
successful install, Luna recommends checking for updates, updating installed
packs, or uninstalling the pack. A dry run shows only its plan.

```powershell
luna discover
luna install dotnet-project --dry-run
luna install dotnet-project
```

Use `id@version` to choose an exact release:

```powershell
luna install dotnet-project@1.0.0 --dry-run
```

The dry run resolves the pack and preflights the target changes without
modifying project files or LunaPack state. Remove `--dry-run` when the plan is
ready to apply.

## Read the Result

Dry runs group source changes, managed-file actions, and lifecycle actions
under labeled sections. Their ASCII action markers remain meaningful when
terminal color is unavailable. Lifecycle script details also identify whether
policy, command arguments, persisted trust, or confirmation controls consent.

A successful install lists its managed-file changes. Use
`--no-file-change-output` to hide that list in routine or automated runs. This
option does not hide managed-file actions from `--dry-run`.

## Continue from the basic install

- Use [parameters and variables](parameters-and-variables.md) when a pack asks
  for values.
- Review [lifecycle hooks](lifecycle-hooks.md) before allowing pack-provided
  scripts.
- Use [target remapping](remap-targets.md) for repository-specific locations.
- Follow [external-source approval](advanced/approve-external-sources.md) when
  the preview proposes additional Git sources.
- Use [adoption](advanced/adopt-existing-files.md) only for existing files that
  exactly match pack content.

The [command reference](cli/commands.md#pack-lifecycle) lists every install
option and interaction.
