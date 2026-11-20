# Luna pack lifecycle commands

Install, preview, update, inspect, and remove installed packs.

## `luna install`

```text
luna install <pack-reference>... [options]
```

Key options include `--dry-run`, `--destination`, `--adopt-existing`,
`--parameter`, `--prompt-parameters`, `--skip-parameters`, `--no-variables`,
`--scripts`, `--skip-instructions`, `--remap-directory`, `--remap-file`,
`--save-remap`, `--accept-sources`, and `--allow-hidden-characters`.

Install resolves and applies each requested pack. `--dry-run` performs planning
without file or state changes. Script modes are `prompt`, `run`, and `skip`.

## `luna update`

```text
luna update [<pack-reference>...] [options]
```

Key options include `--prompt`, `--dry-run`, `--remove-modified`,
`--parameter`, `--scripts`, `--skip-instructions`, remapping options,
`--accept-sources`, and `--allow-hidden-characters`.

Locally modified managed files that are no longer declared are preserved by
default. `--remove-modified` deletes them explicitly.

## `luna uninstall`

```text
luna uninstall <pack-reference>... [options]
```

Uninstall removes unchanged managed files owned only by the selected packs.
Locally modified files are preserved. Lifecycle hooks come from the exact
installed release, not a newer source release.

## `luna outdated`

```text
luna outdated [options]
```

| Option              | Behavior                                         |
| ------------------- | ------------------------------------------------ |
| `--offline`         | Compare cached metadata without remote checks.   |
| `--format <format>` | Select `text` or `json` machine-readable output. |

## Preview and failure behavior

Dry runs do not write files or project state and do not execute lifecycle
scripts. Required parameters, hidden Unicode findings, external source approval,
resource limits, conflicts, and trust policies can stop planning before
mutation.
