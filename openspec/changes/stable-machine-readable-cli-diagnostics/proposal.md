# Proposal

## Why

Scripts and CI integrations currently parse human-readable CLI output, which is
fragile and provides no stable diagnostic identifiers. Add a versioned JSON
contract while preserving the existing text format as the default.

## What Changes

- Add `--format text|json` to root `luna audit`, `luna outdated`, and
  `luna validate <pack-reference>`. Keep text as the default and preserve its
  existing presentation.
- Define a shared versioned JSON envelope with command-specific results. Add
  stable finding codes and summary counts to audit first; migrate other command
  diagnostics incrementally.
- Keep JSON output parseable on standard output and route human-readable
  diagnostics to standard error.
- Document the output contract and its additive, same-major compatibility
  policy.

## Capabilities

### New Capabilities

- `cli-diagnostics`: Define format selection and the versioned machine-readable
  diagnostic contract for the supported CLI commands.

### Modified Capabilities

None. Existing command behavior and text output remain governed by their
current capabilities.

## Impact

The change affects the CLI root and command handlers, audit report formatting,
console output routing, and unit and integration tests under
`projects/cli/src/`. It adds public CLI documentation, an internal compatibility
ADR, and an Unreleased CLI changelog entry. No new dependency is expected.
