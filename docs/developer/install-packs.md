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
modifying project files or LunaPack state. It asks for every configurable pack
parameter before planning so conditional references, files, hooks, and sources
reflect the selected values. For a noninteractive preview, add
`--skip-parameters`. Luna still uses declared defaults, variables, composite
bindings, and explicit `--parameter` values; the preview fails when a required
parameter remains unresolved. Remove `--dry-run` when the plan is ready to
apply.

Luna screens final rendered managed-file text before hooks or project changes.
High-confidence bidi controls and U+200B ZERO WIDTH SPACE stop the install;
other Unicode format characters produce warnings. Every finding shows the
effective project-relative target and a scalar-based location. Review the
bytes before using `--allow-hidden-characters`; the option permits the
operation but does not hide retained findings or an omission summary. NUL or
invalid UTF-8 bytes do not block alone, but recoverable hidden controls do.

Pass `--prompt-parameters` on a real install to answer every configurable
parameter instead of only unresolved required parameters. Optional prompts
offer their declared defaults.

## Direct roots and composite dependencies

By default, discovery and search include raw packs and composite packs. A
composite has one or more `packs` references; a raw pack has none. Use
`--only-composites` when browsing for composite packs only.

`directInstallable: false` marks a catalog release as dependency-only. Luna
does not install that release as a requested root, even when you name its exact
version. An unversioned install can still select an older directly installable
release. A directly installable composite can depend on a dependency-only
release because dependency resolution uses the complete catalog. See the
[manifest reference](packs/reference/manifest.md#direct-installability) for the
metadata's default and compatibility limits.

## Read the Result

Dry runs group source changes, managed-file actions, and lifecycle actions
under labeled sections. Their ASCII action markers remain meaningful when
terminal color is unavailable. Lifecycle script details also identify whether
policy, command arguments, persisted trust, or confirmation controls consent.

A successful install lists its managed-file changes. Use
`--no-file-change-output` to hide that list in routine or automated runs. This
option does not hide managed-file actions from `--dry-run`.

If the selected pack ID and version exist in multiple configured sources, both
the dry run and successful install identify the chosen source by name and type.
The earliest configured source wins an equal-version tie.

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
