---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0104: Require Authoritative Git Manifests

## Context and Problem Statement

Git source discovery cached parsed `pack.yml` content under the consuming
project. ADR-0069 validated cache structure and domain identity, but a
well-formed cache replacement could still provide different lifecycle hooks
or managed-file declarations while claiming the same source fingerprint and
resolved commit. Treating the cache as authoritative can therefore turn
project-writable derived state into pack authorization input.

## Decision Drivers

- Online resolution must derive manifests and default branches from the
  configured Git source.
- Project-local cache files cannot authorize scripts or workspace mutations.
- Keep offline cached browsing available for presentation and recovery.
- Detect cache-to-commit disagreement before hook planning and snapshot use.
- Preserve exact-commit lock behavior without introducing signing-key state.

## Considered Options

- Trust cache entries that pass model validation.
- Sign or encrypt project-local cache entries with a machine-local key.
- Treat cache entries as non-authoritative and reload authoritative manifests.

## Decision Outcome

Chosen option: "Treat cache entries as non-authoritative and reload
authoritative manifests," because it binds online behavior directly to the
configured source and immutable commit without adding a second key boundary.

Online Git discovery resolves an explicit configured ref or the remote HEAD
without reading a cached default branch. It rediscoveres `pack.yml` content
from the resolved commit before catalog resolution and overwrites the derived
cache. Cached manifests may support explicitly cached/offline browsing, but
must not supply authoritative install, update, validation, or trust decisions.

Git materialization independently reloads the checked-out `pack.yml` through
the bounded manifest reader and compares its semantic JSON representation with
the manifest used for resolution. A mismatch aborts the operation before hook
authorization or managed-file planning.

Cache entries used for explicitly cached browsing are still bounded as derived
input. Their manifest byte counts must equal the canonical embedded manifest
size, and the aggregate cannot exceed the compiled catalog ceiling. A
project-writable count cannot therefore reduce the measured retained content.

### Consequences

- Good, because project-writable cache data cannot substitute lifecycle or
  managed-file semantics for an exact Git commit.
- Good, because checked-out content is verified again at the materialization
  boundary.
- Good, because offline browsing retains previously valid derived data.
- Bad, because online discovery reloads manifest content instead of taking a
  fast cache hit for an unchanged commit.
- Bad, because materialization performs an additional bounded manifest read.

### Confirmation

Git unit tests poison a structurally valid cache with substituted hooks and a
cached default branch, then verify that online discovery uses remote-HEAD and
the authoritative manifest. Materialization tests reject a checked-out
manifest that differs from the previously resolved manifest.

## Pros and Cons of the Options

### Trust model-valid cache entries

- Good, because unchanged-commit discovery is fast.
- Bad, because a same-project actor can replace trusted semantics while
  retaining valid IDs, versions, paths, and source fingerprints.

### Sign cache entries

- Good, because modification is detected without requerying Git.
- Bad, because key storage, migration, backup, and rotation add a new trust
  boundary to disposable derived state.

### Reload authoritative manifests

- Good, because Git commit content remains the authority for every online
  lifecycle decision.
- Bad, because online discovery repeats bounded remote and manifest work.

## More Information

This decision supersedes
[ADR-0069](0069-revalidate-git-source-cache-entries.md) and implements the
cache-integrity finding from
[issue #83](https://github.com/lunarisdigitalsolutions/lunapack/issues/83).
