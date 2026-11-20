# `luna mv`

Move or rename an owned managed file while retaining lock ownership.

## Syntax

```text
luna mv <source> <target> [--save-remap]
```

## Arguments

| Argument   | Behavior                                 |
| ---------- | ---------------------------------------- |
| `<source>` | Current project-relative managed target. |
| `<target>` | New project-relative managed target.     |

## Options

| Option              | Behavior                                 |
| ------------------- | ---------------------------------------- |
| `--save-remap`      | Persist the move as a project remapping. |
| `--workspace`, `-w` | Select another project.                  |

The source must remain within the selected workspace and must be owned by a
resolved pack or Luna Link. The command performs one state and filesystem
change; locally modified or conflicting targets stop the move.

## Example

```powershell
luna mv docs/guide.md docs/architecture/guide.md --save-remap
```
