# Design

## Context

See `proposal.md` for motivation and the spec delta for observable behavior.
Lifecycle authorization currently reduces script approval to an authorized
invocation, while the executor owns process start, elapsed time, cancellation,
and exit status. User-local state already lives in a separate `.lunapack`
directory, with owner-only path security and atomic replacement patterns.
Native AOT serialization uses the shared source-generated
`LunapackJsonContext`.

## Goals / Non-Goals

**Goals:**

- Preserve the actual authorization source through execution.
- Record bounded, sanitized metadata for each script-process attempt.
- Keep history private to the current user and separate from project state and
  `config.yml`.
- Make a history write best-effort without changing hook or rollback behavior.

**Non-Goals:**

- Record instruction hooks or script declarations that do not execute.
- Record project paths, environment values, process output, or arbitrary
  lifecycle diagnostics.
- Guarantee detection of every secret value in user-supplied arguments.
- Add a history-clearing command or configurable retention setting.

## Decisions

### Store a bounded per-user JSONL sidecar

Write one JSON object per line to
`~/.lunapack/lifecycle-hook-history.jsonl`. Keep the newest 100 entries and
evict older entries. Serialize the entry type through `LunapackJsonContext` to
preserve Native AOT compatibility. Do not add properties to `config.yml` or
write history into a project.

Use the existing user-settings directory validation and security helpers.
Serialize updates with an exclusive lock, write the retained entries to a
temporary file, apply current-user-only permissions, then atomically replace
the history file. A malformed existing history file is not overwritten as if
it were empty. Missing history is an empty result for reads and does not create
a file.

An entry contains only the fields required by the spec: lifecycle event,
resolved executable, pack ID and version, UTC timestamp, sanitized argument
vector, authorization source, duration, outcome, and exit code when available.
Do not capture the process environment or project path.

### Carry authorization provenance to the executor

Have the authorization decision retain its source rather than recomputing it
after a script runs:

- `--scripts run` records the invocation-level source.
- Prompt mode records every matching persisted trust scope.
- An accepted prompt records interactive confirmation.
- Denied, skipped, conditionally ignored, and instruction hooks produce no
  history entry.

Pass this metadata with the authorized script to the executor. The executor
measures from immediately before process start and creates an outcome for
successful exit, nonzero exit, cancellation, or process-start failure.

### Redact only the persisted argument vector

Before creating an entry, redact values paired with the case-insensitive
secret-like names defined by the spec, in both separate-value and
`name=value` forms. Persist only the sanitized argument vector; never copy
environment values or process output into history. This does not detect
arbitrary secrets embedded in other arguments, so public documentation must
state the limitation.

### Keep the history command read-only

Register `luna hooks history` as a CLI command. Default to the 20 newest
entries; validate `--limit` as an integer from 1 through 100. Render sanitized
fields newest first. An absent file yields an empty state without creating
storage. Invalid or inaccessible data returns an explicit non-success result.
Users delete history by removing the sidecar file.

Render lifecycle consent commands, descriptions, warnings, and history fields
through terminal-safe presentation that converts control, format,
line-separator, paragraph-separator, and bidirectional override characters to
visible escape notation. Apply this encoding only at output boundaries so the
child process receives the original literal argument vector.

### Isolate persistence failures

Represent expected history read and write failures explicitly. The executor
warns when an entry cannot be saved, then returns the original hook result
unchanged. A history failure must not trigger a retry, suppress the hook
failure, or alter existing lifecycle rollback behavior. The read command
surfaces errors instead of treating invalid data as empty.

### Alternatives considered

- **Add history to `config.yml`:** rejected because history is independent
  user-local state and changing the existing settings format risks older strict
  CLI readers.
- **Store history in the project:** rejected because the record is user-local
  and should not become shared or committed project state.
- **Use terminal output or CI logs:** rejected because they do not provide a
  complete durable record of local executions.
- **Omit arguments entirely:** rejected because the requested history includes
  arguments. Best-effort key-based redaction is used instead, with its limits
  documented.

## Risks / Trade-offs

- **Pattern-based redaction can miss arbitrary secrets** -> Recognize common
  secret-like argument names, never capture environment or output, protect the
  file with current-user-only permissions, and document the residual risk.
- **Persisted or pack-controlled text can contain terminal controls** ->
  Encode unsafe characters at consent, history, warning, and error output
  boundaries and keep process arguments unchanged.
- **A user-wide history does not identify which project produced an entry** ->
  Keep project paths out of the record to avoid collecting sensitive local
  paths; use pack, version, event, and timestamp for identification.
- **Concurrent CLI processes can race while pruning entries** -> Serialize
  read/append/prune/replace through an exclusive lock. Lock or write failure
  follows the warning-only history-write behavior.
- **Corrupt history cannot be safely presented or pruned** -> Return a visible
  read error and preserve the file rather than silently discarding records.
- **A 100-entry cap may not retain older investigations** -> Allow users to
  display up to all retained entries and document the fixed cap.

## Migration Plan

No existing data migrates. Create the sidecar lazily on the first script
process attempt. Keep project configuration and lock files unchanged. If the
feature is rolled back, the sidecar remains user data and can be removed
manually.
