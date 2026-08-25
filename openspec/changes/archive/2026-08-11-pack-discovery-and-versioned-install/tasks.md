# Pack Discovery and Versioned Install Tasks

## 1. Schema and Versioning Foundation

- [x] 1.1 Add centrally pinned `NuGet.Versioning` and reference it from the CLI project for Semantic Versioning parsing and precedence comparison.
- [x] 1.2 Add the optional `description` field to the pack manifest model and `projects/schema/pack.schema.json` without invalidating existing manifests.
- [x] 1.3 Extend manifest-schema tests to accept descriptions and retain compatibility for manifests that omit them.

## 2. Source Catalog and Resolution

- [x] 2.1 Replace the exact-path local lookup with a source-type-dispatched catalog boundary that retains validated pack metadata, pack root, source path, and configured source order.
- [x] 2.2 Implement recursive local-source discovery for `pack.yml`, excluding parse or schema-invalid candidates while returning a non-success result for source-enumeration failures.
- [x] 2.3 Implement deterministic catalog version resolution for explicit `id@version` requests, highest-version defaults, and equal-version source precedence using `NuGet.Versioning`.
- [x] 2.4 Add focused unit tests for nested pack roots, arbitrary manifest directory names, malformed candidates, empty sources, duplicate releases, prerelease ordering, and source-precedence resolution.

## 3. CLI Catalog and Install Commands

- [x] 3.1 Add `lunapack discover` using the shared catalog to emit one ordinal-ID-sorted latest-version result per package with an optional 80-character description preview.
- [x] 3.2 Add `lunapack search <term>` using the shared catalog to match full metadata, apply the specified deterministic relevance tiers and tie-breakers, and emit every matching release.
- [x] 3.3 Extend `lunapack install` argument parsing for `id@version` and route both versioned and unversioned installs through shared catalog resolution while preserving existing transactional copy and rollback behavior.
- [x] 3.4 Add CLI unit and integration coverage for compact search and discovery output, description truncation, relevance ordering, explicit-version installation, latest-version installation, and unavailable-version immutability.

## 4. Documentation and Architecture Records

- [x] 4.1 Update developer CLI command guidance for search, add a discover-command reference and command-index entry, and document `install id@version` plus latest-version behavior.
- [x] 4.2 Update developer pack-manifest guidance for optional descriptions and recursively discovered `pack.yml` roots.
- [x] 4.3 Update internal source-provider architecture guidance and create ADR-0014 from the repository template, then register it in the ADR index, to record source-specific catalog browsing and deterministic SemVer resolution.
- [x] 4.4 Review product documentation for current local-pack catalog claims and update it where the new user-visible command behavior changes an existing statement.

## 5. Validation

- [x] 5.1 Run the focused CLI unit and integration tests plus the full `Lunapack.slnx` test suite.
- [x] 5.2 Run schema validation, .NET formatting or analyzers, documentation linting, `openspec validate --strict`, and `git diff --check`.
