---
status: accepted
date: 2026-08-17
---

# ADR-0019: Use installed Git for pack sources

## Context and Problem Statement

LunaPack must consume packs from Git repositories without adding a Git client
library, retaining repository content, or allowing a branch change to alter an
in-progress installation. Local sources remain supported and must retain their
current semantics.

## Decision Drivers

- Preserve reproducible pack provenance.
- Avoid shell interpretation of source-controlled input.
- Transfer no history or unrelated pack content.
- Keep temporary transport state outside project configuration.

## Considered Options

- Invoke the installed Git executable with an argument-safe process boundary.
- Use a Git client NuGet package.
- Clone repositories into configured project source directories.

## Decision Outcome

Chosen option: "Invoke the installed Git executable with an argument-safe
process boundary", because it preserves portable source configuration without
adding a transport dependency or retaining repository clones.

Git commands use `ProcessStartInfo.ArgumentList` with shell execution disabled,
bounded diagnostics, cancellation, timeout enforcement, and process-tree
cleanup. Discovery resolves every source to a commit SHA before reading trees.
It fetches shallow filtered content, reads manifests with tree commands, and
caches validated catalog metadata under `.lunapack/git-sources`. Installation and
update sparse-check out each selected pack at the resolved commit in a unique
directory under the .NET environment temporary-directory root. The workspace is
removed after planner input has been captured.

Git lock provenance records repository URL, configured ref and path when set,
and the immutable resolved commit. No credentials, command lines, Git objects,
or pack content are retained in the metadata cache.

### Consequences

- Good, because branch movement cannot change pack content during one operation.
- Good, because local source ranking and lifecycle behavior remain shared.
- Good, because temporary workspaces do not pollute consuming projects.
- Bad, because Git must be installed and compatible with shallow filtered fetches.
- Bad, because every catalog operation resolves remote state before cache reuse.

### Confirmation

Unit tests cover process failures, ref parsing, source paths, and cache handling.
Real-local-Git integration tests cover default-branch discovery, sparse
materialization, lifecycle state, and immutable lock provenance.

## More Information

This decision extends [ADR-0014](0014-adopt-source-dispatched-pack-catalog.md)
and [ADR-0016](0016-split-portable-configuration-from-resolved-lock-state.md).
