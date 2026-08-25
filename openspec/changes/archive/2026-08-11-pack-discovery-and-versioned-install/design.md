# Pack Discovery and Versioned Install Design

## Context

The current CLI uses `LocalPackDiscovery.Find` to probe one assumed path,
`<source>/<pack-id>/pack.yml`, and returns the first valid match. Pack
manifests already require a SemVer-formatted version, but have no description.
The source-provider architecture reserves catalog and version-listing behavior
for each provider, while the current project-manifest schema supports only the
`local` source type. See [proposal.md](proposal.md) for the motivation and the
capability deltas for observable behavior.

## Goals / Non-Goals

**Goals:**

- Build one deterministic catalog path used by local search, discovery, and
  installation.
- Support arbitrarily nested `pack.yml` roots in a local source without
  changing existing direct-child source layouts.
- Use a standards-compliant SemVer implementation for latest-version and
  explicit-version selection.
- Keep the optional description schema-compatible with existing manifests.

**Non-Goals:**

- Add remote source types, remote transport, indexes, caching, or background
  refresh.
- Support version ranges, dist-tags, package updates, simultaneous installed
  versions, or lock-file generation.
- Accept `pack.yml` as an alias; `pack.yml` remains the repository's canonical
  manifest filename.

## Decisions

### Dispatch catalog browsing by source type

Introduce an internal catalog-browser boundary that maps a source type to its
browse strategy. The first implementation handles the existing `local` type
and recursively enumerates `pack.yml` candidates through `IFileSystem`.
Command handlers and installation call the catalog boundary rather than
embedding source-specific traversal.

This preserves a stable place for future browse strategies without widening
the version-1 project-manifest schema to unsupported source types. Adding
recursive traversal directly to each command would duplicate validation,
provenance, and resolution rules; implementing the full documented async
provider interface now would introduce unrelated transport and lifecycle work.

### Retain validated catalog entries with provenance

The local browser returns entries containing the parsed `PackManifest`, pack
root, fully resolved source path, and configured source position. A file named
`pack.yml` is a candidate only; YAML parse or schema-validation failures
exclude that candidate so one malformed pack does not hide other valid packs.
An inability to enumerate a configured source remains a command error because
the resulting catalog would be incomplete.

The catalog retains duplicate IDs and versions. `discover` groups by ID and
selects the latest version; `search` lists every matching release; installation
uses source position to break equal-version ties. This makes each command's
aggregation explicit and lets users find older releases by ID before requesting
one with `@version`.

### Use NuGet.Versioning for SemVer precedence

Add `NuGet.Versioning` through central package management and use it to parse
the schema-accepted version values and compare Semantic Versioning precedence.
The resolver accepts an `id` or `id@version`; no-version requests select the
highest parsed version, including a prerelease when no higher candidate exists.
An explicit version must be available in the catalog.

`System.Version` does not model SemVer prerelease or build metadata, and a
custom comparator would duplicate a mature compatibility-sensitive algorithm.

### Make catalog output compact and deterministic

Use one shared formatter that writes a line containing ID, version, and, when
present, a description preview. Truncate previews to 80 characters including
the ASCII `...` marker. `discover` sorts its one-per-ID results by ordinal ID.
`search` applies the specified metadata relevance tiers after invariant-case
normalization, then the specification tie-breakers. Full descriptions remain
available to ranking even when displayed previews are truncated.

This deliberately favors predictable scripting output over a rich table or
source provenance display. A later machine-readable output format can be added
without changing catalog semantics.

### Keep the manifest schema and documentation aligned

Add an optional string `description` property to the schema and `PackManifest`.
No project- or pack-manifest version migration is necessary because omitting
the new property stays valid. Update developer CLI command and pack-manifest
guidance, the source-provider architecture guidance, and ADR-0014 to record
the catalog-browser boundary and resolution policy.

## Risks / Trade-offs

- [Large local sources make recursive browsing slow] -> Scan only on command
  invocation and defer caching/indexing until measured usage needs it.
- [Malformed packs may be less visible when excluded] -> Preserve command
  failure for source-enumeration errors and cover candidate exclusion with
  tests; diagnostic reporting can be added separately.
- [Duplicate releases can make search output repetitive] -> Keep search
  complete by release while `discover` provides a concise latest-only view.
- [SemVer package dependency increases restore surface] -> Pin the package
  centrally and verify resolution behavior with isolated unit tests.

## Migration Plan

1. Add the backward-compatible description property and model support.
2. Replace the exact-path finder with the source-dispatched local catalog and
   SemVer resolver while preserving existing installation transaction logic.
3. Add command, resolution, schema, and CLI-output tests before documenting
   the commands.
4. Update developer and internal documentation, add ADR-0014, then run the
   repository's .NET, schema, documentation, and OpenSpec validation gates.

Rollback consists of reverting the CLI and schema changes; existing manifests
without descriptions and installed-pack records retain their current shape.
