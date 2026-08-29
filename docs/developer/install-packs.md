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

For installation customization, see [Parameters and variables](parameters-and-variables.md),
[Lifecycle hooks](lifecycle-hooks.md), and [Remap managed targets](remap-targets.md).

When the graph selects pack-defined external Git content, the preview shows
reused source mappings, proposed workspace source additions, whether approval is
required, and resulting file actions. Interactive installation asks once for
all missing sources. Use `--accept-sources` for conflict-free automation; an
identifier conflict still requires explicit source configuration.

## Lifecycle scripts

Packs can declare `preInstall`, `postInstall`, `preUpdate`, and `postUpdate`
hooks. Select their behavior explicitly:

```powershell
luna install dotnet-project --scripts prompt
luna install dotnet-project --scripts run
luna install dotnet-project --scripts skip
```

`prompt` is the default. It uses existing source or source-plus-pack trust and
otherwise asks for each hook. `run` approves non-suppressed hooks for this
invocation only. `skip` runs no hooks. A dry run prints planned hook order,
suppression, consent mode, and locked sources without prompting or starting a
process.

Persisted script denial overrides these modes and all grants. A denied install
warns for every skipped script before hook or file processing, keeps
instructions, and continues installing managed files. Dry runs label those
scripts `policy-denied` with all denying scopes.

Grant persistent trust only after reviewing a source:

```powershell
luna trust source engineering
luna trust pack dotnet-project --source engineering
```

Trusted hooks run with your user account's authority. Review pack hook
descriptions and argv before allowing execution. Read the
[security model](threat-model.md) before granting trust to a new publisher.

## Portable paths

Use `/` for paths in `lunapack.yml`, `lunapack-lock.yml`, and `pack.yml`.
LunaPack also accepts `\` in project configuration, pack manifest selectors and
targets, and CLI path arguments. It normalizes those inputs before planning
filesystem work and writes configuration and lock-file paths with `/`, on every
supported operating system.

Use [Remap managed targets](remap-targets.md) to configure repository-specific
locations, omit selected content, or move installed files while retaining lock
ownership.
