---
status: accepted
date: 2026-08-18
decision-makers: LunaPack maintainers
---

# ADR-0023: Validate CLI Locks Before Runtime Builds

## Context and Problem Statement

The CLI releases for multiple runtime identifiers and commits a single NuGet
lock file. Restoring with one runtime identifier creates lock data that is
incompatible with another runtime identifier in locked mode. CI must validate
the full lock file while still compiling a selected release target.

## Decision Drivers

- Keep one reviewed lock file for all supported CLI release targets.
- Detect lock-file drift locally and in CI before compilation.
- Allow a pipeline to build a selected runtime identifier.

## Considered Options

- Restore in locked mode for only the selected runtime identifier.
- Disable locked restore for runtime-specific builds.
- Restore all CLI runtime identifiers in locked mode, then build the selected runtime without restoring.

## Decision Outcome

Chosen option: "Restore all CLI runtime identifiers in locked mode, then build
the selected runtime without restoring," because it validates the shared lock
data without allowing the selected runtime to narrow the restore graph.

The CLI declares every supported target in `RuntimeIdentifiers`. Local build
commands and CI restore without `--runtime` and with locked mode. They pass the
selected runtime identifier only to subsequent `--no-restore` build or publish
commands.

### Consequences

- Good, because locked restore verifies every declared target before a pipeline builds one target.
- Good, because local and CI build paths use the same lock-validation sequence.
- Bad, because an intentional dependency change requires an explicit lock-file refresh.

### Confirmation

Run `dotnet restore projects/cli/src/Lunapack.slnx --locked-mode`, then run a
runtime-specific CLI build with `--no-restore`. GitHub Actions performs both
steps for every CLI workflow invocation.

## Pros and Cons of the Options

### Restore in locked mode for only the selected runtime identifier

- Good, because each restore evaluates less runtime metadata.
- Bad, because its narrowed runtime graph conflicts with the shared lock file.

### Disable locked restore for runtime-specific builds

- Good, because it avoids the immediate mismatch.
- Bad, because CI could silently rewrite or accept unreviewed dependency resolution.

### Restore all CLI runtime identifiers, then build without restoring

- Good, because it preserves one lock file and validates all targets.
- Bad, because runtime-specific commands must consistently use `--no-restore`.
