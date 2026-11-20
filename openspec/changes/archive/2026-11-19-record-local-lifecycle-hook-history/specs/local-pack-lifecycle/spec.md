# Spec Delta

## ADDED Requirements

### Requirement: Record lifecycle script execution attempts

Luna SHALL record each lifecycle script process attempt, including attempts
that fail to start, are cancelled, or exit unsuccessfully. Each entry SHALL
identify the lifecycle event, resolved executable, pack ID and version, UTC
timestamp, sanitized arguments, duration in milliseconds, outcome, and numeric
exit code when available. Instruction hooks and scripts not executed because
they are skipped, denied, or conditionally ignored SHALL not be recorded.

#### Scenario: Record a successful invocation

- **WHEN** a lifecycle script executes successfully under `--scripts run`
- **THEN** history records the event, executable, pack and version, sanitized
  arguments, invocation-level `--scripts run` authorization source, duration,
  successful outcome, and exit code

#### Scenario: Record an unsuccessful invocation

- **WHEN** a script process fails to start, is cancelled, or exits
  unsuccessfully
- **THEN** history records the corresponding outcome and includes a numeric
  exit code when the process provides one

#### Scenario: Omit hooks that do not execute

- **WHEN** a script is skipped, denied, or conditionally ignored, or an
  instruction hook is displayed
- **THEN** Luna creates no history entry for that hook

### Requirement: Identify the script authorization source

Each recorded entry SHALL identify whether the script was authorized by
invocation-level `--scripts run`, persisted trust, or interactive confirmation.
For persisted trust, the entry SHALL identify every applicable project,
local-user, or global-user scope.

#### Scenario: Record persisted trust scopes

- **WHEN** persisted trust authorizes a lifecycle script from multiple scopes
- **THEN** the history entry identifies each applicable trust scope

#### Scenario: Record interactive confirmation

- **WHEN** a user explicitly confirms an otherwise untrusted lifecycle script
- **THEN** the history entry identifies interactive confirmation as its
  authorization source

### Requirement: Bound and protect per-user history

Luna SHALL store one per-user history at
`~/.lunapack/lifecycle-hook-history.jsonl`, retain at most 100 entries, and evict
the oldest entries first. The history file SHALL be accessible only to the
current user and SHALL not contain project paths or process environment values.

#### Scenario: Evict the oldest entry at capacity

- **WHEN** a new history entry is added while 100 entries are already retained
- **THEN** Luna retains the newest 100 entries and removes the oldest entry

#### Scenario: Exclude project paths and environment values

- **WHEN** Luna records a lifecycle script execution
- **THEN** the history entry contains neither the project path nor process
  environment values

### Requirement: Redact secret-like arguments before persistence

Before storing arguments, Luna SHALL replace values associated with
case-insensitive argument names containing `password`, `passwd`, `secret`,
`token`, `credential`, `authorization`, `auth`, `api[-_]?key`, or
`access[-_]?key` with `[REDACTED]`. Redaction SHALL cover separate-value and
`name=value` forms. Other arbitrary secret values may remain and SHALL be
documented as a limitation.

#### Scenario: Redact separate and assigned secret values

- **WHEN** a script argument contains `--token value` or `api_key=value`
- **THEN** the stored arguments replace each associated value with
  `[REDACTED]`

### Requirement: Preserve hook results when history writes fail

If Luna cannot persist a history entry, it SHALL warn without changing the
hook's success or failure result or the lifecycle operation's existing
rollback behavior.

#### Scenario: Preserve a successful hook result

- **WHEN** a script succeeds but writing its history entry fails
- **THEN** Luna warns about the history failure and preserves the successful
  hook result

#### Scenario: Preserve a failed hook result

- **WHEN** a script fails and writing its history entry also fails
- **THEN** Luna warns about the history failure and preserves the original
  script failure result

### Requirement: Inspect recent lifecycle history

`luna hooks history` SHALL display entries newest first, showing the newest 20
by default. `--limit <count>` SHALL accept integers from 1 through 100. The
command SHALL be read-only, SHALL not create or alter the history file, and
SHALL provide no clear operation. Users SHALL delete history by removing the
sidecar.

#### Scenario: Read history when no file exists

- **WHEN** a user runs `luna hooks history` before any entry has been recorded
- **THEN** Luna reports an empty history without creating the sidecar

#### Scenario: Limit displayed entries

- **WHEN** a user runs `luna hooks history --limit 5` with at least five entries
- **THEN** Luna displays the five newest entries in newest-first order without
  changing the file

#### Scenario: Report invalid or unreadable history

- **WHEN** the history file cannot be read or contains invalid data
- **THEN** the command returns a non-success result and does not report an
  empty successful history

#### Scenario: Delete history manually

- **WHEN** a user removes the history sidecar and runs `luna hooks history`
- **THEN** Luna reports an empty history without recreating the sidecar

### Requirement: Render lifecycle commands and history safely

Luna SHALL render pack-controlled lifecycle command text, descriptions, and
persisted history fields without emitting terminal control characters. Control,
format, line-separator, paragraph-separator, and bidirectional override
characters SHALL appear as visible escape notation in CLI output. Safe rendering
SHALL not change the original argument vector delivered to a process.

#### Scenario: Escape terminal controls before consent

- **WHEN** a lifecycle command or description contains ANSI, line-control, or
  bidirectional control characters
- **THEN** the consent prompt displays visible escapes and does not emit the
  controls to the terminal

#### Scenario: Escape terminal controls from persisted history

- **WHEN** a valid history entry contains terminal control characters
- **THEN** `luna hooks history` displays visible escapes, and the history file
  remains unchanged

#### Scenario: Preserve executable arguments

- **WHEN** safe rendering escapes a lifecycle argument
- **THEN** the script process still receives the original literal argument
