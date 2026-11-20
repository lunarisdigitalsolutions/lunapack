# `luna hooks history`

Read the bounded local lifecycle-script attempt history.

## Syntax

```text
luna hooks history [--limit <count>]
```

## Options

| Option              | Default           | Behavior                                 |
| ------------------- | ----------------- | ---------------------------------------- |
| `--limit <count>`   | `100`             | Return at most this many newest entries. |
| `--workspace`, `-w` | Current directory | Select another project.                  |

## Entry fields

Each entry records timestamp, event, pack ID and version, executable, sanitized
argument vector, authorization source, trust scopes, duration, outcome, and
exit code. Secret-like argument values are redacted before persistence.

## Behavior

History is user-local and bounded to 100 entries. Read operations do not mutate
project state or history. Persistence failure warns and does not change a script
success or rollback result.
