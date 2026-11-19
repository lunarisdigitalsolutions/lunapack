---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0095: Store local lifecycle hook history in a user-local sidecar

## Context and Problem Statement

Luna users could see lifecycle-script output while a command ran, but had no
bounded durable record of the attempted command, pack release, authorization
source, duration, or outcome. Project configuration and lock files must remain
portable, and lifecycle history must not expose project paths or process
environment values.

## Decision Drivers

- Inspect recent local script attempts without changing lifecycle results.
- Keep per-user state compatible with older strict configuration readers.
- Restrict history to the current user and preserve it across CLI commands.
- Bound storage growth and handle corrupt or inaccessible state explicitly.
- Keep permission, lock, and replacement behavior testable and cross-platform.

## Considered Options

- Store bounded JSON Lines history in a dedicated user-local sidecar.
- Add history to `~/.lunapack/config.yml`.
- Store history in each project.
- Keep history in terminal or CI logs only.

## Decision Outcome

Chosen option: "Store bounded JSON Lines history in a dedicated user-local
sidecar," because lifecycle history is independently versioned per-user state,
can remain owner-only, and does not alter portable project configuration or
existing strict settings readers.

Luna writes `~/.lunapack/lifecycle-hook-history.jsonl` with the newest 100
entries. Each process attempt records its event, resolved executable, pack ID
and version, UTC timestamp, sanitized arguments, authorization provenance,
duration, outcome, and available exit code. Writers serialize updates through
an exclusive file lock, write a secure temporary file, and replace the retained
JSON Lines file atomically. Reads do not create or alter state.

History never captures the working directory, process environment, or child
output. Pack-file argument paths use their manifest-relative form, and absolute
project-path occurrences are represented as `[PROJECT]`. Conventional
secret-like argument names receive best-effort value redaction before
persistence.

### Consequences

- Good, because recent local lifecycle attempts are inspectable and bounded.
- Good, because history remains separate from `config.yml`, project state, and
  source-control files.
- Good, because corrupt data is reported rather than silently discarded, and
  write failure cannot change an already-observed script result.
- Bad, because the retained entries do not include a project path for
  disambiguation.
- Bad, because pattern-based argument redaction cannot identify every arbitrary
  secret value; users must continue to avoid passing secrets as arguments when
  possible.
- Bad, because entries older than the 100-entry cap are unavailable for later
  review.

### Confirmation

Unit tests verify owner-only permissions, missing and corrupt state, bounded
retention, concurrent writers, redaction, all process outcomes, output
encoding, and read-only command behavior. CLI integration coverage verifies
history recording alongside existing lifecycle rollback behavior.

## More Information

The corresponding product contract is governed by the
`record-local-lifecycle-hook-history` OpenSpec change. This decision does not
add a clear-history command; users delete history by removing the sidecar.
