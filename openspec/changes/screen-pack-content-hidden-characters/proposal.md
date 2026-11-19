# Proposal

## Why

Pack text can contain invisible or direction-changing Unicode characters that reviewers and automated consumers may miss. Issue #80 calls for consistent diagnostics before pack content is installed or published, plus a read-only way to inspect installed content.

## What Changes

- Add pack-content screening that reports each finding's path, 1-based line, Unicode-scalar column, code point, and severity.
- Scan valid UTF-8 authored source files during local pack validation and publication, final rendered planned text during installation and update, and installed files during audit. Skip binary and invalid UTF-8 payloads.
- Begin with warning-only diagnostics. After false-positive review, block only high-confidence bidi controls and zero-width spaces for mutating and validation operations; keep other hidden format characters as warnings.
- Preserve valid emoji joiners and variation selectors. Provide an explicit command-line override for the later blocking phase, proposed as `--allow-hidden-characters`.
- Keep audit read-only and report-only. Document diagnostic output, blocking, override, and exit behavior for CLI users.

## Capabilities

### New Capabilities

- `pack-content-screening`: Detect and report hidden Unicode in pack text, with staged warning and blocking behavior.

### Modified Capabilities

- `local-pack-lifecycle`: Screen install/update plans before mutation and report installed-file findings during audit.
- `pack-authoring`: Include screening diagnostics in `luna pack validate` and define its staged exit behavior.
- `pack-publishing`: Screen pack content before upload and define later high-confidence blocking behavior.

## Impact

The change affects CLI pack planning, author validation, publication preflight, and audit output, with focused unit and integration coverage. Public developer guidance and the CLI changelog must describe the observable behavior; the accepted compatibility and rollout decision may require an internal ADR.
