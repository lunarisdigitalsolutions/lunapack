---
status: accepted
date: 2026-08-21
decision-makers: [LunaPack maintainers]
---

# ADR-0037: Canonicalize persisted project paths with forward slashes

## Context and Problem Statement

LunaPack reads project configuration, lock state, pack manifests, and CLI
arguments on both Windows and Linux. Native path separators can make the same
logical path serialize differently and, on Linux, a backslash from a pack
manifest can be interpreted as a filename character instead of a separator.

## Decision Drivers

- Keep project configuration and lock state portable across supported hosts.
- Accept existing Windows-authored project and pack inputs.
- Ensure planning and ownership compare one canonical path representation.

## Considered Options

- Preserve the host-native separator in persisted state.
- Reject backslash-separated input outside Windows.
- Accept both separators and serialize persisted state with forward slashes.

## Decision Outcome

Chosen option: "Accept both separators and serialize persisted state with
forward slashes", because it preserves compatibility while giving configuration
and lock files one stable cross-platform representation.

Project-state loading and saving normalize configuration source paths, requested
destinations, remapping keys and values, lock source and pack locations,
declared and effective managed targets, and Git repository paths. Pack manifest
source, directory, glob, and target paths normalize before validation and
filesystem planning. CLI path arguments normalize at their command boundaries.

### Consequences

- Good, because Windows-authored configuration and packs operate on Linux.
- Good, because configuration and lock diffs use one portable path syntax.
- Bad, because a successful state write can reserialize accepted legacy
  backslash paths.

### Confirmation

State-store tests load and rewrite Windows-style configuration and lock paths.
Lifecycle tests install packs and destination arguments using backslashes, then
verify slash-only persisted configuration and lock files.

## More Information

Related: [ADR-0036](0036-record-declared-and-effective-managed-targets.md) and
the [project configuration specification](../../../../openspec/specs/cli-project-configuration/spec.md).
