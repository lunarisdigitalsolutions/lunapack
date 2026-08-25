## Why

Teams need to consume governed packs maintained in remote Git repositories without copying them into a local source tree. LunaPack currently supports only local sources, preventing version-pinned remote distribution and reproducible provenance for those packs.

## What Changes

- Add Git repositories as configurable pack sources, with an optional ref and repository subpath.
- Add a Git-backed catalog that resolves a default branch, discovers `pack.yml` files, reads available pack versions, and materializes only requested pack directories.
- Invoke only the locally installed Git executable through a cross-platform, argument-safe process wrapper; do not add a Git client NuGet dependency.
- Add configurable per-source Git operation timeouts, capped by a five-minute default, and persistent source metadata caching under `.lunapack`.
- Record Git repository URL, requested ref, source subpath, and immutable resolved commit in `lunapack-lock.yml`.
- Extend configuration and lock schemas, CLI source registration, tests, developer reference material, and internal source-provider guidance.

## Capabilities

### New Capabilities

- `git-pack-sources`: Configure, discover, cache, resolve, and materialize versioned packs from remote Git repositories.

### Modified Capabilities

- `cli-project-configuration`: Register Git sources through the CLI and persist their configuration.
- `pack-catalog`: Browse Git-source pack manifests alongside configured local sources.
- `local-pack-lifecycle`: Install and update packs resolved from Git sources while preserving source precedence and provenance.
- `manifest-schemas`: Support Git source configuration and Git-specific lock provenance.
- `project-lockfile`: Persist immutable Git resolution evidence for installed packs.

## Impact

- Affected CLI source commands, configuration models, catalog/discovery, installation/update materialization, lock persistence, and test infrastructure.
- Affected schemas: `lunapack.yml` and `lunapack-lock.yml`; configuration source entries gain a Git variant without changing existing local-source entries.
- Affected documentation: developer configuration, command and manifest references; internal source-provider and caching/lifecycle guidance.
- Requires a locally available `git` executable at runtime; adds no NuGet package dependency.
