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

Grant persistent trust only after reviewing a source:

```powershell
luna trust source engineering
luna trust pack dotnet-project --source engineering
```

Trusted hooks run with your user account's authority. Review pack hook
descriptions and argv before allowing execution.

## Portable paths

Use `/` for paths in `lunapack.yml`, `lunapack-lock.yml`, and `pack.yml`.
LunaPack also accepts `\` in project configuration, pack manifest selectors and
targets, and CLI path arguments. It normalizes those inputs before planning
filesystem work and writes configuration and lock-file paths with `/`, on every
supported operating system.

## Remap managed targets

Keep portable pack targets unchanged and configure repository-specific layout
in `lunapack.yml`:

```yml
remap:
  directories:
    docs/adr: docs/internal/01-architecture/decisions
  files:
    docs/adr/template.md: docs/adr/_template.md
```

Directory mappings retain any descendant path. Exact file mappings take
precedence over directory mappings. All keys and values must be non-empty,
project-relative paths that resolve within the project.

Set or replace a reusable mapping without editing YAML directly:

```powershell
luna remap set directory docs/adr docs/internal/01-architecture/decisions
luna remap set file docs/adr/template.md docs/adr/_template.md
```

`luna remap set <directory|file> <target> <newTarget>` updates the matching
global mapping in `lunapack.yml`. `luna remap list` shows configured mappings,
and `luna remap rm <directory|file> <target>` removes one. A mapping change
affects future installations and new files introduced by updates; it does not
move an installed file. Use `luna mv` for lock-backed relocation.

Use an invocation-only mapping for a single installation:

```powershell
luna install madr-adr-template --remap-directory docs/adr=docs/internal/01-architecture/decisions
luna install madr-adr-template --remap-file docs/adr/template.md=docs/adr/_template.md
```

Repeat either option to map more targets. Command-line file mappings take
precedence over global file mappings; command-line directory mappings take
precedence over global directory mappings. `--destination` cannot be combined
with either remapping option.

LunaPack records both the manifest-declared target and its effective target in
`lunapack-lock.yml`. Updates and uninstalls use that recorded effective target,
so changing a global mapping does not move an installed file. `luna inspect`
shows applicable global mappings as `declared -> effective`.

Relocate an installed managed file explicitly:

```powershell
luna mv docs/adr/template.md docs/architecture/adr/_template.md
```

The command moves one uniquely owned managed file when the source exists and
the target does not. When a consumer has already moved the source and the
target exists, it rebinds lock ownership without changing file contents. It
rejects unsafe paths, ownership conflicts, and states where both files exist.
