# `luna init`

Create the project configuration and lock file for the selected workspace.

## Syntax

```text
luna init [options]
```

## Options

| Option                          | Behavior                                                      |
| ------------------------------- | ------------------------------------------------------------- |
| `--workspace <directory>`, `-w` | Select a project directory.                                   |
| `--log-level <level>`, `-ll`    | Set `verbose`, `debug`, `info`, `warning`, or `error` output. |
| `--suppress-next-steps`         | Suppress contextual next-step guidance.                       |

## Behavior

`luna init` creates `lunapack.yml` and `lunapack-lock.yml` when project state
does not already exist. Existing project state remains unchanged. Initialization
adds `/.lunapack/` to a project-root `.gitignore` while preserving existing
entries.

The command does not install packs or add sources. The next common step is
`luna sources add local|git|oci`.

## Exit behavior

- `0`: project state initialized or already initialized.
- `1`: project state could not be initialized or validated.

## Example

```powershell
luna init
luna sources add local engineering ./packs
```
