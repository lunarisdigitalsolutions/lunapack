# Remap managed targets

Keep pack targets portable and map them to your repository layout during
installation. Remapping changes where Luna writes managed files without
changing the pack manifest.

## Configure reusable mappings

Set project mappings through the CLI:

```powershell
luna remap set directory docs/adr docs/internal/architecture/decisions
luna remap set file docs/adr/template.md docs/adr/_template.md
luna remap list
```

Directory mappings retain descendant paths. Exact file mappings take
precedence. `luna remap rm <directory|file> <target>` removes a mapping.

A mapping affects future installations and files newly introduced by updates.
It does not move an installed file.

## Remap one installation

Use repeatable command options for invocation-only mappings:

```powershell
luna install madr-adr-template --remap-directory docs/adr=docs/internal/architecture/decisions
luna install madr-adr-template --remap-file docs/adr/template.md=docs/adr/_template.md
```

Command-line mappings take precedence over project mappings of the same type.
`--destination` cannot be combined with either remapping option.

Add `--save-remap` to merge the command-line mappings into `lunapack.yml` after
a successful installation:

```powershell
luna install madr-adr-template --remap-directory docs/adr=docs/internal/architecture/decisions --save-remap
```

The saved mappings apply to later installs. A failed installation leaves the
project mappings unchanged. `--save-remap` requires at least one command-line
mapping.

## Ignore pack targets

Use `@ignore` as the exact mapping value to exclude a declared file or every
file below a declared directory:

```yml
remap:
  directories:
    docs/generated: '@ignore'
  files:
    .github/dependabot.yml: '@ignore'
```

For one installation, pass the same value through a remapping option. Add
`--save-remap` to retain it:

```powershell
luna install github-actions-pr-gate@1.0.0 --remap-directory .github=@ignore --save-remap
```

Ignored files are not written and receive no managed-file lock entry. An exact
file mapping still takes precedence over a matching ignored directory, so it
can retain or relocate one file below that directory.

When an update adds an ignore mapping, Luna leaves an existing file unchanged
but removes its pack or link ownership from the lock. When an ignored target
has no local file and its ignore mapping is later removed, a subsequent update
can write and manage it again. `@ignore` is case-sensitive and reserved as a
special mapping target.

Luna records declared and effective targets in `lunapack-lock.yml`. Updates and
uninstalls continue using the recorded effective target even if project
mappings later change, except when `@ignore` explicitly removes ownership.
`luna inspect` shows applicable project mappings as `declared -> effective`.

## Move installed files

Relocate one managed file and update lock ownership:

```powershell
luna mv docs/adr/template.md docs/architecture/adr/_template.md
```

Use a managed directory as the source to move every managed file below it while
preserving descendant paths:

```powershell
luna mv docs/adr docs/architecture/adr
```

If files were already moved manually and only their targets exist, the same
command rebinds ownership without changing content. The operation validates all
targets before moving anything and rolls back the batch if persistence fails.
Luna rejects escaping paths, ownership conflicts, overlapping source and target
directories, and states where both forms of a managed path exist.

Add `--save-remap` to record the relocation as a reusable file or directory
mapping. Luna derives its source from each lock record's manifest-declared
target, so future installs use the new location.

Use `/` in persisted LunaPack documents. CLI path input accepts either path
separator and stores canonical project-relative paths.
