# `luna pack`

Create and maintain a local `pack.yml` for pack authors.

## Syntax

```text
luna pack init
luna pack add [managed file|hook|reference] ...
luna pack set <author|description|homepage|id|license|name|version> <value>
luna pack remove <selector>
luna pack list
luna pack show
luna pack hooks
luna pack sources
luna pack validate [--allow-hidden-characters]
```

## Managed content

Use `luna pack init` to create a pack directory and manifest. Add managed files
and lifecycle declarations from the repository using the `add` subcommands.
The authoring commands validate the candidate before writing it.

Lifecycle script hooks support:

- `luna pack add hook script command`
- `luna pack add hook script file`
- `luna pack add hook instruction`
- `luna pack remove hook`

Composite references use `luna pack add reference`.

## Hidden Unicode

`luna pack validate --allow-hidden-characters` continues after reviewing
high-confidence hidden Unicode findings. Omit the option to block before
publishing or validation side effects.

## Example

```powershell
luna pack init
luna pack set id example-guidance
luna pack set version 1.0.0
luna pack validate
```

See [Pack manifest reference](../../packs/reference/manifest.md) and
[Release a pack](../../packs/how-to/release-a-pack.md).
