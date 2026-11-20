---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0098: Define Strict Audit Exit Codes

## Context and Problem Statement

The default LunaPack audit reports resolved pack, source, and link state without
failing for managed-file drift. Automation needs an opt-in integrity gate that
distinguishes missing or modified locked targets from invalid usage and
unreadable project state without changing the existing report-only command.

## Decision Drivers

- Let CI distinguish managed-content drift from an audit that could not run.
- Preserve the current default audit output and exit behavior.
- Use the installed lock file as the integrity authority without network
  resolution or project mutation.
- Keep the strict-mode contract stable across process integrations.

## Considered Options

- Return the same nonzero exit code for every strict-audit failure.
- Return distinct codes for clean state, integrity violations, and operation
  errors.
- Keep strict audit report-only and require callers to parse text output.

## Decision Outcome

Chosen option: "Return distinct codes for clean state, integrity violations,
and operation errors," because CI can respond to managed-file drift separately
from invalid invocation or unreadable state without parsing presentation text.

`luna audit --strict` returns:

- `0` when all recorded pack and link targets match their locked SHA-256
  digests.
- `2` when one or more targets are missing or have modified content.
- `1` when command usage is invalid or project or target state cannot be read.

`--fail-fast` requires `--strict` and stops after the first missing or modified
target. Source-provenance and ownership-conflict statuses remain informational.
The lock-file schema and default audit behavior do not change.
Strict audit uses text output. Combining `--strict` with `--format json` is
invalid usage and returns exit code `1` with a JSON failure envelope; JSON
output remains available for report-only audit.

### Consequences

- Good, because process callers can separate integrity drift from operation
  failure.
- Good, because strict auditing remains read-only and independent of pack-source
  availability.
- Bad, because exit code `2` gains a command-specific meaning that consumers
  must document and preserve.

### Confirmation

CLI process tests exercise clean, aggregate-violation, fail-fast, invalid-usage,
default-compatibility, and unreadable-state outcomes. The public command
reference and audit guide publish the same exit-code contract.

## Pros and Cons of the Options

### Distinct strict-audit outcome codes

- Good, because automation can identify the failure category directly.
- Good, because it does not alter default audit compatibility.
- Bad, because integrations must learn a LunaPack-specific integrity code.

### One nonzero failure code

- Good, because callers need fewer process-exit branches.
- Bad, because repository drift and an unreadable workspace look identical.

### Parse strict-audit output

- Good, because the process exits remain minimal.
- Bad, because presentation changes can silently break CI behavior.

## More Information

This decision applies only to opt-in strict audit. Other commands retain their
existing exit-code contracts.
