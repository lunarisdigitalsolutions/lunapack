# `luna links`

Select regular files from configured local or Git sources without creating a
pack manifest.

## Syntax

```text
luna links add <name> [options]
luna links list
luna links show <name>
luna links remove <name>
```

## `luna links add`

| Option            | Behavior                                        |
| ----------------- | ----------------------------------------------- |
| `--source`, `-s`  | Configured local or Git source name.            |
| `--include`, `-i` | Repeatable file, directory, or glob selector.   |
| `--exclude`, `-e` | Repeatable exclusion glob.                      |
| `--path`          | Source-relative selector base.                  |
| `--target`, `-t`  | Workspace-relative output directory.            |
| `--ref`           | Git ref override.                               |
| `--strip-prefix`  | Remove a source path prefix from targets.       |
| `--flatten`       | Place selected files directly under the target. |
| `--install`       | Install the link after saving its definition.   |
| `--force`         | Replace an existing link definition.            |

At least one include is required. Link names use exact case-sensitive
alphanumeric/hyphen syntax.

## State behavior

Link intent remains in `lunapack.yml`. Selected paths, source identity,
resolved Git commit, effective targets, and SHA-256 digests remain in
`lunapack-lock.yml`.

Removing an installed link requires uninstall or `--force` according to lock
ownership. Use `luna audit --strict` to verify installed link targets.

## Example

```powershell
luna links add agents --source engineering --include ".github/agents/*.agent.md" --target ".github/agents" --install
```
