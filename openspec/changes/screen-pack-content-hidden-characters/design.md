# Design

## Context

See `proposal.md` for motivation and the spec deltas for observable behavior. Installation planning produces rendered managed-file content; update planning determines final write and merge content. Author validation resolves declared managed-file selections, publication validates an archive before upload, and audit reads installed targets and their lock-file evidence.

## Goals / Non-Goals

**Goals:**

- Apply one deterministic scanner and diagnostic model at each specified lifecycle boundary.
- Inspect the exact text that an operation will write, validate, publish, or report.
- Keep scanning independent from file I/O, command output, and lifecycle policy.
- Preserve current persisted configuration, lock-file, and manifest formats.

**Non-Goals:**

- Scan pack manifests, project configuration, script commands, or lifecycle instruction hooks.
- Change pack selection, template behavior, managed-file ownership, or publication packaging.
- Add a user-configurable policy setting or suppress findings when an override is used.

## Decisions

### Use one pure scanner with operation-specific inputs

Place the scanner and immutable finding values under a focused pack-content-screening feature. The scanner accepts a path label and payload bytes and returns findings; callers remain responsible for selecting files, reading bytes, presenting diagnostics, and deciding whether findings block an operation. Do not add a package dependency or direct console/filesystem access to the scanner.

The lifecycle caller supplies final planned bytes after template rendering and merge planning. Author validation and publication supply raw source bytes selected by managed-file declarations, including resolved external selections. Audit supplies existing installed managed targets. This catches parameter values introduced by rendering while also checking the authored source that may be distributed.

### Classify text without extension allowlists

Stream strict UTF-8 decoding without extension allowlists. Recognize leading UTF-8, UTF-16LE, UTF-16BE, UTF-32LE, and UTF-32BE BOMs and decode BOM-tagged text with a strict decoder. For unmarked invalid UTF-8, resynchronize one invalid byte at a time and continue classifying later valid scalars. Do not insert replacement characters into findings; NUL and invalid bytes do not fail an operation by themselves, but later recoverable hidden controls remain enforceable.

Walk decoded Unicode scalars once while tracking 1-based line and scalar-column positions. Treat CRLF as one line break; also recognize LF and CR. For invalid-byte recovery, each invalid byte occupies one scan position. Format code points as uppercase `U+` hexadecimal, padding values below `U+10000` to four digits. Emit slash-separated source-relative paths for author validation/publication and effective project-relative paths for install, update, and audit.

### Keep the high-confidence set explicit

Use the Unicode `Bidi_Control` property set and U+200B ZERO WIDTH SPACE as the high-confidence set. Report other `Format` category code points as warnings. Preserve a U+200D only when it participates in a valid emoji ZWJ sequence; use Unicode Extended_Pictographic/Extend data pinned to the CLI's targeted Unicode data version rather than treating every joiner as safe. Exempt variation selectors only when they follow a valid emoji or keycap base; report orphan selectors as warnings.

This boundary is intentionally narrower than blocking every invisible character. It supports warning review without making legitimate format characters in authored text an automatic installation failure.

### Integrate before side effects and keep audit report-only

Run lifecycle scanning after final content planning but before hook execution or any project mutation. Scan validation and publication inputs before reporting validation success or uploading the archive. Keep `luna audit` read-only: scan existing managed targets and append findings to its report without changing its success status.

Use one formatter for findings, escaping path and character names through the existing CLI console boundary. In warning-only rollout, render every finding as a warning. In the later enforcement phase, high-confidence findings fail install, update, pack validation, and publication before side effects; warnings remain non-blocking. `--allow-hidden-characters` permits those operations to continue but leaves diagnostics visible. The override is command-scoped and is not persisted.

Reject terminal controls, Unicode `Format` characters, line separators, and paragraph separators in project-relative and managed manifest paths. Escape those characters for legacy persisted diagnostics. Retain at most 1,000 findings per payload and retain separate high-confidence and omitted-count evidence so resource bounds cannot create an enforcement bypass.

### Gate enforcement on warning review

Ship warning-only behavior first. Review findings from maintained packs and focused fixtures, correct false positives, and verify valid emoji sequences before enabling enforcement in a separate atomic implementation commit. The phase gate is a release/implementation sequence, not a runtime setting. Record the accepted compatibility and override decision in a new ADR because enforcement changes lifecycle and publication outcomes.

## Risks / Trade-offs

- Opaque payloads can contain byte sequences that resemble text controls. Scan recoverable Unicode scalars while failing only on actual hidden findings, and verify NUL, invalid UTF-8, and BOM cases end to end.
- Scanning merged output can report a hidden character already present in consumer-owned target text. This reflects the final content LunaPack would write; findings identify the effective target and remain warnings unless high confidence.
- Emoji property data can become stale as Unicode evolves. Pin the data version, cover known valid and invalid sequences, and review updates deliberately.
- Runtime Unicode category tables may differ between supported runtimes. Keep the high-confidence ranges explicit and test warning classification against the supported CLI runtime baseline.

## Migration Plan

No persisted data migration is required. First enable diagnostics without changing exit codes or operation success. After reviewing and tuning findings, enable high-confidence blocking with the explicit command-line override. If enforcement causes unacceptable false positives, revert the enforcement commit while retaining warning diagnostics; no project state needs rollback.
