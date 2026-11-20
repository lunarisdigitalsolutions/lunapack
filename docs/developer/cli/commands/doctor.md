# `luna doctor`

Diagnose LunaPack project state, Git availability, credentials, and local
profile/cache directories.

## Syntax

```text
luna doctor [options]
```

## Options

| Option                          | Behavior                                                                  |
| ------------------------------- | ------------------------------------------------------------------------- |
| `--network`                     | Check configured Git and OCI sources with bounded, noninteractive probes. |
| `--workspace <directory>`, `-w` | Select a project directory.                                               |
| `--log-level <level>`, `-ll`    | Set output verbosity.                                                     |
| `--suppress-next-steps`         | Suppress contextual next-step guidance.                                   |

## Behavior

The default run is offline. It does not create missing project or profile
directories. `--network` enables source checks but does not change project
configuration or lock state.

Each result has a pass, warning, or failure status and includes remediation
when available. Source or credential warnings do not imply that local project
state was mutated.

## Exit behavior

- `0`: all required checks pass.
- `1`: one or more checks fail.

## Example

```powershell
luna doctor
luna doctor --network
```
