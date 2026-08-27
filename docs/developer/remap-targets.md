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

Luna records declared and effective targets in `lunapack-lock.yml`. Updates and
uninstalls continue using the recorded effective target even if project
mappings later change. `luna inspect` shows applicable project mappings as
`declared -> effective`.

## Move an installed file

Relocate one uniquely owned managed file and update lock ownership:

```powershell
luna mv docs/adr/template.md docs/architecture/adr/_template.md
```

If the file was already moved manually and only the target exists, the same
command rebinds ownership without changing its content. Luna rejects escaping
paths, ownership conflicts, and states where both paths exist.

Use `/` in persisted LunaPack documents. CLI path input accepts either path
separator and stores canonical project-relative paths.
