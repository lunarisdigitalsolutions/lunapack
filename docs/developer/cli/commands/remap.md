# `luna remap`

Persist project-wide managed-file directory and file remappings.

## Syntax

```text
luna remap list [options]
luna remap set <directory|file> <target> <new-target> [options]
luna remap remove <directory|file> <target> [options]
```

## Arguments

| Argument              | Behavior                           |
| --------------------- | ---------------------------------- |
| `<directory-or-file>` | Remapping kind.                    |
| `<target>`            | Declared target path or directory. |
| `<new-target>`        | Effective project-relative target. |

## Precedence

The effective target in `lunapack-lock.yml` has highest priority. For a new
target, the match order is command-line remapping, installed pack entry
remapping, top-level project remapping, then the declared pack target.

Use `luna install --save-remap` or `luna update --save-remap` to persist a
command remapping on the requested pack entry.

## Example

```powershell
luna remap set directory docs docs/architecture
luna remap set file README.md docs/README.md
luna remap list
```
