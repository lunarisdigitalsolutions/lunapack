# `luna audit`

Inspect resolved pack and link state, source drift, and managed-file integrity.

## Syntax

```text
luna audit [--strict] [--fail-fast] [--format text|json]
```

## Options

| Option              | Behavior                                                                         |
| ------------------- | -------------------------------------------------------------------------------- |
| `--strict`          | Fail when a managed target is missing or differs from its locked SHA-256 digest. |
| `--fail-fast`       | Stop strict auditing after the first integrity violation.                        |
| `--format <format>` | Select `text` or `json` machine-readable output.                                 |

## Exit behavior

- Text audit: `0` for a completed report.
- Strict audit: `2` when one or more targets are missing or modified.
- Strict audit: `1` for invalid usage or unreadable state.
- JSON audit: no strict mode; JSON and text diagnostics do not share a strict
  JSON mode.

Machine-readable JSON is emitted on standard output; human-readable
diagnostics are emitted on standard error.
