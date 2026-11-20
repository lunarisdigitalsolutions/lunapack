# `luna sbom`

Export lock-derived dependency inventory from a LunaPack project.

## Syntax

```text
luna sbom --target <target> [--output <path>]
```

## Targets

| Target          | Format             |
| --------------- | ------------------ |
| `cyclonedx-1.6` | CycloneDX 1.6 JSON |
| `cyclonedx-1.7` | CycloneDX 1.7 JSON |
| `spdx-2.3`      | SPDX 2.3 JSON      |
| `spdx-3.0.1`    | SPDX 3.0.1 JSON    |

## Options

| Option                | Behavior                                                                          |
| --------------------- | --------------------------------------------------------------------------------- |
| `--target <target>`   | Required inventory format.                                                        |
| `--output <path>`     | Project-relative JSON output file. Omit for standard output.                      |
| `--log-level <level>` | Debug diagnostics go to standard error; SBOM JSON stays clean on standard output. |
| `--workspace`, `-w`   | Select another project.                                                           |

The inventory derives from `lunapack-lock.yml`. It is not a signature or
attestation. Existing protected output files are preserved on failure.

## Example

```powershell
luna sbom --target cyclonedx-1.6
luna sbom --target spdx-3.0.1 --output reports/lunapack-spdx.json
```
