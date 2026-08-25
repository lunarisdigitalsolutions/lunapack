---
status: accepted
date: 2026-08-11
decision-makers: [Lunaris Digital Solutions]
---

# ADR-0014: Adopt Source-Dispatched Pack Catalog

## Context and Problem Statement

Early local installation probed one assumed path for a pack ID. That prevented
discovery, search, version selection, nested layouts, and consistent provenance
across commands. LunaPack needs deterministic local catalog behavior now without
committing version-1 project manifests to remote provider protocols.

## Decision Drivers

- Reuse one validated source-browsing path for discovery, search, and install.
- Preserve existing local source compatibility while allowing nested pack roots.
- Resolve versions with standards-compliant Semantic Versioning precedence.
- Leave a narrow extension point for later source types.

## Considered Options

- Use a source-dispatched catalog boundary with a local browser.
- Add recursive path traversal independently in every command.
- Implement the full asynchronous provider contract before catalog support.

## Decision Outcome

Chosen option: "Use a source-dispatched catalog boundary with a local browser",
because it centralizes validation, provenance, and deterministic resolution
without adding remote transport work.

### Consequences

- Good, because all catalog commands and install share one source-specific path.
- Good, because each catalog entry retains pack root, source path, and source order.
- Good, because `NuGet.Versioning` supplies Semantic Versioning precedence.
- Bad, because recursive local scans happen for every catalog operation until a
  measured need justifies caching.

### Confirmation

Focused unit tests cover nested roots, invalid candidates, empty sources,
prerelease ordering, and equal-version source precedence. Integration tests
cover discover, search, and explicit/latest/unavailable install behavior.

## Pros and Cons of the Options

### Use a source-dispatched catalog boundary with a local browser

- Good, because future source types get a dedicated browsing strategy.
- Bad, because the initial abstraction supports only one source type.

### Add recursive path traversal independently in every command

- Good, because it has no new catalog type.
- Bad, because validation and resolution behavior would drift between commands.

### Implement the full asynchronous provider contract before catalog support

- Good, because it would match the long-term provider shape immediately.
- Bad, because it adds unsupported transport and lifecycle scope.

## More Information

See the [source guide](../../../developer/sources.md) and the
[pack discovery OpenSpec change](../../../../openspec/changes/archive/2026-08-11-pack-discovery-and-versioned-install/design.md).
