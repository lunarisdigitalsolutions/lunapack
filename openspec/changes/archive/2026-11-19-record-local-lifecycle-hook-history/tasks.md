# Tasks

## 1. User-local history storage

- [x] 1.1 Add the history entry model and secure bounded JSONL store, including
      source-generated serialization, locking, atomic replacement, 100-entry
      retention, and explicit read/write failures; verify focused store tests cover
      missing and corrupt files, permissions, retention, concurrent writes, and
      persistence failures.
- [x] 1.2 Add case-insensitive redaction for secret-like argument names in
      separate-value and `name=value` forms; verify focused tests cover each form,
      preserve non-secret arguments, and redact values before persistence.
- [x] 1.3 Record the accepted storage and retention decision in a new internal
      ADR and add it to the ADR index; verify the ADR is accepted and indexed.

## 2. Lifecycle execution recording

- [x] 2.1 Preserve authorization provenance from lifecycle authorization
      through execution, including `--scripts run`, every applicable persisted
      trust scope, and interactive confirmation; verify focused tests assert each
      source and scope.
- [x] 2.2 Record process start failures, cancellation, nonzero exits, and
      successful exits with duration and available exit code; warn on history
      write failure without changing the hook result or rollback behavior, and
      verify lifecycle tests cover each outcome and failure path.
- [x] 2.3 Add terminal-safe presentation for pack-controlled consent and
      persisted history fields; verify security tests cover ANSI, line-control,
      separator, and bidirectional controls while preserving process arguments.
- [x] 2.4 Update `docs/developer/lifecycle-hooks.md` with recorded fields,
      per-user retention, authorization sources, and redaction limitations; verify
      documented behavior matches the lifecycle spec.

## 3. Read-only history command

- [x] 3.1 Implement and register `luna hooks history [--limit <count>]` with
      newest-first output, default limit 20, accepted range 1 through 100, and
      explicit read errors; verify command tests cover empty history, ordering,
      limits, invalid input, corrupt data, and no file mutation.
- [x] 3.2 Update `docs/developer/cli/commands.md` and
      `projects/cli/CHANGELOG.md`; verify command syntax, defaults, deletion
      guidance, and redaction limitations match the implementation.

## 4. Integration verification

- [x] 4.1 Run `dotnet build projects/cli/src/Lunapack.slnx` and
      `dotnet test projects/cli/src/Lunapack.slnx`; verify both pass with lifecycle
      history serialization and command coverage included.
