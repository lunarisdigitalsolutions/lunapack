# Design

## Context

The lock file already records target paths and SHA-256 digests for pack- and
link-managed files. The current audit reports pack and source information,
checks external pack files and link targets, but ordinary pack-managed targets
have no integrity status. The command keeps findings informational and returns
success for findings, with nonzero behavior for audit or project-state operation
errors.

## Goals / Non-Goals

**Goals:**

- Verify the complete lock-file inventory of pack and link targets when strict
  mode is requested.
- Return structured target findings so reporting, fail-fast behavior, and exit
  status use the same results.
- Preserve existing audit output and behavior unless `--strict` is supplied.

**Non-Goals:**

- Repair files, rewrite lock state, or resolve pack sources during an audit.
- Change lock-file schema or classify source drift and ownership conflicts as
  content-integrity violations.

## Decisions

### Use the lock file as the integrity inventory

Strict verification reads `lunapack-lock.yml` and checks each recorded pack and
link target against its recorded digest. It does not resolve current pack
sources or infer expected content from a newer release; the lock file describes
the installed content being audited. This also keeps strict audit independent
of network availability.

The alternative is to re-resolve pack sources during audit. That would compare
against current source content rather than the installed snapshot and could
require network access.

### Keep strict verification structured and read-only

The audit layer returns owner, target path, and integrity outcome for every
checked file. The command handler renders those results and applies the
requested scan mode; it does not parse existing console output to determine
success. Ownership-conflict and source-provenance statuses remain informational.

Only missing targets and digest mismatches are integrity violations. A target
read failure is an operation error, not evidence of modified content, and
returns exit code `1`.

### Make strict behavior opt-in

`--strict` activates integrity exit behavior on the existing `luna audit`
command. Without it, current output and exit behavior remain unchanged.
`--fail-fast` is a modifier of strict mode and stops after the first missing or
mismatched target; usage and state-read errors remain operation errors.

A separate strict-audit command would duplicate the existing audit workflow and
its output. An opt-in flag preserves the current command surface for existing
consumers.

### Keep exit categories stable

Strict audit returns `0` for a clean scan, `2` for one or more missing or
modified targets, and `1` for invalid usage or unreadable state. The distinct
integrity code lets CI distinguish workspace drift from an audit that could
not run. Record this durable CLI compatibility contract in an ADR and public
command guidance.

## Risks / Trade-offs

- **Full scans add file I/O:** Strict mode is opt-in; `--fail-fast` can stop
  after the first integrity violation.
- **Targets can change during inspection:** Treat read failures as operation
  errors with exit code `1`; never silently report a clean result.

## Migration Plan

No persisted data migration is needed. Existing invocations remain
report-only. CI consumers opt into the new contract with `luna audit --strict`
and may add `--fail-fast` when they do not need an aggregate report.
