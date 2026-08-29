# Set parameters and variables

Parameters customize one pack installation. Project variables provide reusable
values to any pack with a matching parameter name.

## Pass installation parameters

Use repeatable `--parameter` or `-p` options:

```powershell
luna install license-mit@1.0.0 \
  --parameter companyName="Lunaris Digital Solutions"
```

Repeated names collect values only when the declaration is an enum with
`multiple: true`. Selection order is preserved. Duplicate selections, values
outside the declared enum, and repeated scalar names fail before mutation.

Required values that are not supplied by command input, a composite pack, or a
matching project variable prompt in an interactive terminal. Parameter values
can control templates and managed-file conditions.

Unattended commands must resolve every required parameter without a prompt.
Supply explicit `--parameter` values, commit compatible project variables, or
use composite bindings before running Luna with noninteractive standard input.
Luna has no separate `--non-interactive` option.

## Reuse project variables

Store a value in `lunapack.yml` through the CLI:

```powershell
luna variables set companyName "Lunaris Digital Solutions"
luna variables list
```

Multi-select variables use unique string arrays in `lunapack.yml`:

```yml
variables:
  features: [api, docker]
```

An optional multi-select parameter with no input, matching variable, composite
binding, or default resolves to `[]`.

Explicit `--parameter` values take precedence over project variables. Disable
all variable matching with `--no-variables`, or exclude selected names with a
repeatable `--skip-variable` option:

```powershell
luna install license-mit@1.0.0 --no-variables
luna install license-mit@1.0.0 --skip-variable companyName
```

Remove a value when the project no longer needs it:

```powershell
luna variables rm companyName
```

Variables are project configuration, while explicit installation inputs are
not persisted as variables. Pack authors define `string`, `bool`, and `enum`
parameters. See [Use Scriban templates](packs/how-to/use-scriban-templates.md)
for declarations, template rendering, conditions, and the managed-file
`files.path` and `files.relative_path` functions. Composite graphs use one
namespace for parameter names; see
[Composition and lifecycle](packs/explanation/composition-and-lifecycle.md) for
declaration and binding precedence.
