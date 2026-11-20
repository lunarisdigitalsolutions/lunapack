# `luna variables`

Set project variables that satisfy matching pack parameters.

## Syntax

```text
luna variables list [options]
luna variables set <name> <value> [options]
luna variables remove <name> [options]
```

## Arguments

| Argument  | Behavior                                                             |
| --------- | -------------------------------------------------------------------- |
| `<name>`  | Variable name. Luna accepts names matching `[A-Za-z_][A-Za-z0-9_]*`. |
| `<value>` | String or Boolean value.                                             |

## Behavior

Variables are stored in `lunapack.yml`. They do not persist explicit install
input and do not replace explicit parameter values or composite bindings.
Install prompts only for required parameters that no source can satisfy.

Use `--no-variables` or `--skip-variable <name>` on lifecycle commands to
exclude variables for one invocation.

## Example

```powershell
luna variables set companyName Contoso
luna variables list
luna variables remove companyName
```
