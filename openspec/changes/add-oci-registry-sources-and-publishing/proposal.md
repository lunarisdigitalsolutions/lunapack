# Proposal

## Why

Tracking feature: [#64](https://github.com/lunarisdigitalsolutions/lunapack/issues/64).

LunaPack consumers and authors need to distribute versioned public and private
packs through OCI registries without Docker or registry-specific tooling.
Private source failures currently stop catalog browsing, making otherwise
accessible packs unavailable to users without access to every configured source.

## What Changes

- Add first-class `oci` sources with separate registry URL and repository path;
  map each pack to its own repository and each version to a tag.
- Use ORAS .NET behind a narrow registry abstraction. Represent a pack as a
  LunaPack OCI artifact containing metadata and one complete ZIP content layer.
- Integrate OCI discovery, inspection, version selection, dependencies,
  installation, dry runs, updates, audit, and removal with existing pipelines.
  Query current registry state directly; do not add a persistent OCI cache.
- Resolve authentication on demand, resume the original operation, securely
  persist validated credentials in native OS stores, isolate credentials per
  configured source, and support noninteractive providers including optional
  Azure identity for ACR. Add no login or logout commands.
- Isolate source availability failures, improve Git authentication diagnostics
  without managing Git passwords, and add project/user-local source ignore state.
- Add `luna publish <folder> --target <source>` with manifest-derived ID/version,
  full validation, shared authentication, and immutable release protection.
- Plan user/developer/CLI documentation, threat-model updates, comprehensive
  tests, and a private-pack example that publishes changed folders in CI.

This change contains planning artifacts only. No CLI, schema, dependency,
workflow, documentation of available features, or accepted ADR is implemented.

## Capabilities

### New Capabilities

- `oci-pack-sources`: Registry-independent transport, repository mapping,
  discovery, artifact format, validated ZIP retrieval, and temporary lifetimes.
- `source-authentication`: Demand-driven OCI credentials, native secure storage,
  source isolation, noninteractive providers, and optional ACR identity.
- `local-source-state`: Project/user-local ignoring across source types without
  changing shared state or silently switching locked sources.
- `pack-publishing`: Source-selected publishing of validated pack directories
  with manifest-derived identity, shared authentication, and immutable versions.
- `ci-pack-publishing`: Documented private-pack repository and changed-pack
  GitHub Actions publishing through the Luna CLI.

### Modified Capabilities

- `cli-project-configuration`: OCI registration, source listing, and rename
  integration with source-local state.
- `manifest-schemas`: Additive OCI source, identity, lock, and user-settings
  contracts with existing configuration compatibility.
- `pack-catalog`: OCI catalog participation and independently reported source
  failures without changing precedence or semantic-version selection.
- `local-pack-lifecycle`: Normal OCI consumption, dependency resolution, trust,
  dry run, updates, offline reporting, and source-required failures.
- `project-lockfile`: Exact OCI provenance and digest-pinned materialization.
- `git-pack-sources`: Authentication diagnostics using existing Git credentials.

## Impact

- Proposed code surfaces: `projects/cli/src/Lunapack.Cli/{Sources,Catalog,Project,
  Packs,Trust,Application}`, CLI composition, command handlers and completion.
  Extend existing source models, identity/fingerprint helpers, `PackCatalog`,
  materialization and lifecycle boundaries; do not build an OCI install engine.
- Proposed contract surfaces: `projects/schema/lunapack.schema.json`,
  `lunapack-lock.schema.json`, `lunapack-user-settings.schema.json`, serialization,
  runtime validators, package-distributed schemas, and their tests.
  `github` remains a CLI shorthand that persists a `git` source.
- Future dependencies: ORAS .NET and optional Azure.Identity; native credential
  adapters to be selected and license/platform-reviewed during implementation.
  No package references are added by this proposal.
- Product documentation: `docs/product` requirements, roadmap, and acceptance
  criteria for registry distribution and private-pack consumption.
- Maintainer documentation: `docs/internal` source/catalog/lifecycle boundaries,
  schemas, credential resolution, security, testing, and proposed ADRs when
  decisions become accepted during implementation.
- Public documentation: `docs/developer/sources.md`, `cli/commands.md`,
  `install-packs.md`, `update-packs.md`, `automation.md`, `github-actions`,
  `threat-model.md`, troubleshooting and authoring guidance; update relevant
  website navigation and schema reference delivery together.
- Tests: focused unit/security/regression coverage plus real-registry CI,
  GHCR/ACR compatibility, and Harbor smoke coverage where practical.
