# Set parameters and variables

Parameters customize one pack installation. Project variables provide reusable
values to any pack with a matching parameter name.

## Pass installation parameters

Use repeatable `--parameter` or `-p` options:

```powershell
luna install company-foundation \
  --parameter companyName="Lunaris Digital Solutions" \
  --parameter projectType=service
```

Required values that are not supplied by command input, a composite pack, or a
matching project variable prompt in an interactive terminal. Parameter values
can control templates and managed-file conditions.

## Reuse project variables

Store a value in `lunapack.yml` through the CLI:

```powershell
luna variables set companyName "Lunaris Digital Solutions"
luna variables list
```

Explicit `--parameter` values take precedence over project variables. Disable
all variable matching with `--no-variables`, or exclude selected names with a
repeatable `--skip-variable` option:

```powershell
luna install company-foundation --no-variables
luna install company-foundation --skip-variable companyName
```

Remove a value when the project no longer needs it:

```powershell
luna variables rm companyName
```

Variables are project configuration, while explicit installation inputs are
not persisted as variables. Pack authors define `string`, `bool`, and `enum`
parameters. See [Use Scriban templates](packs/how-to/use-scriban-templates.md)
for declarations, template rendering, conditions, and the managed-file
`files.path` and `files.relative_path` functions.
