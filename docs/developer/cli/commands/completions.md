# `luna completions`

Generate shell completion configuration for supported shells.

## Syntax

```text
luna completions script [<shell>] [--install]
```

## Supported shells

| Shell      | Registration             |
| ---------- | ------------------------ |
| Bash       | Native completion script |
| Fish       | Native completion script |
| Nushell    | Native completion script |
| PowerShell | Native completion script |
| Zsh        | Native completion script |

Without `--install`, the command prints the script. `--install` previews and
requires confirmation before appending registration to the shell's user
configuration.

Completion covers commands, options, finite values, installed or available
pack IDs, link IDs, source names, and variable names supported by the selected
workspace.
