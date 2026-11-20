# `luna publish`

Publish a validated local pack to a configured OCI source.

## Syntax

```text
luna publish <folder> --target <source> [options]
```

## Arguments

| Argument            | Behavior                                              |
| ------------------- | ----------------------------------------------------- |
| `<folder>`          | Local pack-authoring directory containing `pack.yml`. |
| `--target <source>` | Configured OCI source name.                           |

## Options

| Option                      | Behavior                                          |
| --------------------------- | ------------------------------------------------- |
| `--allow-hidden-characters` | Continue after reviewing hidden Unicode findings. |
| `--workspace`, `-w`         | Select a project directory.                       |

## Behavior

Luna validates the complete authoring tree and pack manifest, applies finite
project resource limits and compiled safety ceilings, creates a pack archive,
and publishes a version tag to the target registry. Publication does not run
pack lifecycle hooks and does not change project configuration or lock state.

## Exit behavior

Invalid manifests, hidden-content findings, resource limit violations,
credentials, existing tags, or registry failures return nonzero and do not
publish a new artifact.

## Example

```powershell
luna publish ./guidance --target registry
```
