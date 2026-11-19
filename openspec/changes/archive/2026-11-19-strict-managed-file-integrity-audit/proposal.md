# Proposal

## Why

`luna audit` reports resolved state but does not fail CI when ordinary
pack-managed files are missing or differ from their lock-file digests. CI needs
an opt-in integrity gate without changing the existing report-only behavior.

## What Changes

- Add `luna audit --strict` to verify every pack- and link-managed target against
  its locked SHA-256 digest.
- Report all missing or modified targets by default. Add `--fail-fast` to stop
  after the first integrity violation.
- Return `0` for a clean strict audit, `2` for missing or modified targets, and
  `1` for invalid usage or unreadable project state.
- Preserve the current report-only behavior when `--strict` is omitted.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-pack-lifecycle`: define strict verification and failure behavior for
  managed-file audits.

## Impact

- CLI audit options, managed-target verification, diagnostics, and exit codes.
- Unit and integration coverage for pack and link targets, parser behavior, and
  strict-mode exit codes.
- Public guidance in `docs/developer/advanced/audit-and-recover.md` and
  `docs/developer/cli/commands.md`, plus an Unreleased CLI changelog entry.
- Internal ADR documenting the durable exit-code contract.
- No lock-file schema or dependency changes.
