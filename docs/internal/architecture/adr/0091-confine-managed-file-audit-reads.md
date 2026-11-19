---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0091: Confine Managed-File Audit Reads

## Context and Problem Statement

Strict audit hashes lock-controlled project paths. Lexical lock validation and
direct file opening are insufficient when a path is drive-relative or crosses a
symbolic link, junction, or reparse point. Such a read can inspect content
outside the selected workspace and turn audit output into an existence or hash
oracle.

## Decision Drivers

- Keep audit reads beneath the selected workspace.
- Reject filesystem aliases at the same boundary used for managed mutations.
- Distinguish an unreadable or unsafe target from modified content.
- Preserve portable, Native AOT-compatible filesystem abstractions.

## Considered Options

- Trust validated lock paths and open them directly.
- Normalize paths and reject existing aliases around an audited read.
- Add platform-specific handle-relative no-follow traversal immediately.

## Decision Outcome

Chosen option: "Normalize paths and reject existing aliases around an audited
read," because it closes deterministic drive-relative and alias escapes using
the existing path authority and portable filesystem abstraction.

Managed-file audit resolves each lock target with
`ProjectPath.NormalizeProjectRelativePath`, verifies existing components with
`ProjectMutationPathSecurity`, opens the target read-only, and rechecks the path
while holding the stream. Unsafe paths, aliases, malformed state, and access
failures are operation errors with exit code `1`; they are not classified as
missing or modified content.

### Consequences

- Good, because deterministic lock-path escapes cannot reach outside the
  workspace.
- Good, because strict audit no longer mistakes access denial for content drift.
- Bad, because alias detection and open remain separate operations. A process
  with the same user identity can still race an entry replacement, matching the
  residual boundary documented by ADR-0071.

### Confirmation

Security and process tests cover drive-relative targets, directory symbolic
links, explicit-null state collections, read failures, and strict exit code `1`.
The audit design and public command reference describe the confinement rule.

## Pros and Cons of the Options

### Normalize and reject aliases

- Good, because the boundary is shared with managed-file mutation checks.
- Good, because it remains portable across supported operating systems.
- Bad, because it does not provide race-free confinement against same-user code.

### Open lock paths directly

- Good, because implementation complexity is minimal.
- Bad, because symlinks and drive-relative paths can escape the workspace.

### Platform-specific no-follow traversal

- Good, because it can provide stronger handle-level confinement.
- Bad, because it duplicates native traversal and identity checks for every
  supported platform.

## More Information

ADR-0071 records the existing residual race and future handle-relative
hardening. ADR-0090 defines the strict-audit exit-code contract.
