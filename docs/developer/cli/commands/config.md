# `luna config`

Set, list, or remove project and user preferences and finite resource limits.

## Syntax

```text
luna config set <key> <value> [scope]
luna config list [scope]
luna config remove <key> [scope]
```

## Subcommands

| Subcommand | Arguments       | Purpose                                              |
| ---------- | --------------- | ---------------------------------------------------- |
| `set`      | `<key> <value>` | Store one preference or resource limit.              |
| `list`     | None            | List explicitly stored values in the selected scope. |
| `remove`   | `<key>`         | Remove one explicitly stored value.                  |

## Scope options

| Option                 | Storage                                          |
| ---------------------- | ------------------------------------------------ |
| `--project`, `--local` | `lunapack.yml` in the selected project.          |
| `--user-project`       | The canonical project entry in the user profile. |
| `--global`             | The global user-settings entry.                  |

Scope options are mutually exclusive. With no scope, `luna config` operates on
global settings. Project scopes require an initialized workspace.

## Supported keys

Boolean preferences:

- `discover.only-composites`
- `search.only-composites`
- `git.allow-insecure-transport`
- `next-steps.enabled`

Resource limits include all `resource-limits.*` keys documented in
[Configuration reference](../configuration-reference.md#resource-limits).
Values are positive integers except Boolean preferences, which accept `true`
or `false`.

## Examples

```powershell
luna config set next-steps.enabled false --project
luna config set git.allow-insecure-transport false --global
luna config set resource-limits.files-per-pack 5000 --project
luna config list --global
luna config remove next-steps.enabled --project
```

## Exit behavior

Unknown keys, invalid values, overlapping scope options, and missing project
state fail without changing settings. `luna config list` reads only the selected
scope and does not include built-in defaults.
