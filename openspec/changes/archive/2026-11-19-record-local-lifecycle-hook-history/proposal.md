# Proposal

## Why

After local lifecycle scripts run, users have no durable record of the command,
pack release, consent source, duration, or outcome. Terminal scrollback and CI
logs are incomplete for local runs; a bounded user-local history makes recent
executions inspectable without changing hook outcomes.

## What Changes

- Record script-process attempts in a separate per-user
  `~/.lunapack/lifecycle-hook-history.jsonl` sidecar, retaining the newest 100
  entries. Include attempts that fail to start, are cancelled, or exit
  unsuccessfully. Do not record instruction hooks or hooks that never execute
  because they are skipped, denied, or conditionally ignored.
- Record the lifecycle event, resolved executable, pack ID and version, UTC
  timestamp, sanitized arguments, consent source, duration, and outcome/exit
  code. Do not store environment values or project paths.
- Add a read-only `luna hooks history [--limit <count>]` command. Show the
  newest 20 entries by default and accept limits from 1 through 100. Keep
  deletion manual by removing the sidecar.
- Redact values associated with conventional secret-like argument names and
  assignments before persistence. Document that pattern-based redaction cannot
  recognize every arbitrary secret.
- Warn when history persistence fails without changing the lifecycle hook
  result. Report history read failures rather than treating corrupt or
  inaccessible data as an empty history.
- Encode terminal control characters when displaying pack-controlled lifecycle
  commands or persisted history, preventing display content from forging user
  consent or CLI output.
- Update public lifecycle and command guidance, the CLI changelog, and focused
  tests when implementing the behavior.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-pack-lifecycle`: Define bounded local history for script execution and
  read-only inspection of recent entries.

## Impact

The CLI lifecycle authorizer, executor, and lifecycle service will supply the
actual consent source and execution outcome to a new history store. The CLI
application will register the read-only command. Unit and integration tests
will cover redaction, retention, persistence failures, execution outcomes, and
command behavior.

No pack manifest, project configuration, lock-file, or dependency contract
changes. The dedicated sidecar keeps history separate from `config.yml`.

The OpenSpec delta defines the planned product behavior. On acceptance,
implementation will update `docs/developer/lifecycle-hooks.md`,
`docs/developer/cli/commands.md`, and `projects/cli/CHANGELOG.md`. An
internal ADR for the accepted storage and retention decision will be added
after review.
