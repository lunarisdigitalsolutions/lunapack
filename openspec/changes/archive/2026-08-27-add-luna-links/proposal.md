# Add Luna Links

## Why

Projects need to consume selected files from Git or local repositories that do not publish LunaPack manifests. Luna Links makes those project-owned selections reproducible and manageable through LunaPack's existing file ownership and lifecycle protections without requiring upstream repositories to adopt LunaPack.

## What Changes

- Add project-local link definitions that select files, directories, and glob matches from an existing configured source, with exclusions and target-path mapping.
- Add `luna links add`, `luna links list`, `luna links show`, and `luna links rm` commands for durable link configuration and inspection.
- Allow `luna install`, `luna update`, `luna uninstall`, `luna outdated`, and `luna audit` to operate on links through the existing managed-file lifecycle.
- Resolve Git-backed links to immutable commits and persist per-file source paths, effective targets, and SHA-256 digests; persist equivalent file evidence for local links.
- Detect selected-file additions, changes, moves, removals, local modifications, definition changes, ownership conflicts, and unsafe paths before mutation.
- Normalize links into the existing internal resolved-pack and managed-file model in memory without creating or persisting synthetic pack manifests.
- Add cache support for immutable Git source content used by links.

## Capabilities

### New Capabilities

- `luna-links`: Defines project-owned source selections, link management commands, selection and mapping semantics, validation, inspection, and lifecycle behavior specific to links.

### Modified Capabilities

- `cli-project-configuration`: Adds durable link definitions to `lunapack.yml`, initializes an empty link collection, and prevents link names from conflicting with installed root pack identifiers.
- `manifest-schemas`: Extends the project and lock-file schemas with compatible link definition and resolved-state shapes.
- `git-pack-sources`: Resolves and materializes arbitrary link-selected Git content at an immutable commit and caches it without requiring a pack manifest.
- `project-lockfile`: Records resolved link provenance, definitions, per-file ownership, source paths, effective targets, and content digests.
- `local-pack-lifecycle`: Extends installation, update, outdated detection, audit, conflict handling, and uninstall semantics to link roots while retaining existing transaction and local-modification protections.

## Impact

- CLI command registration, argument parsing, output, and contextual guidance under `projects/cli/src/Lunapack.Cli`.
- Project configuration, lock-state models, serialization, source providers, selection/globbing, path validation, lifecycle planning, ownership, hashing, and transaction handling.
- JSON Schemas and schema examples under `projects/schema`.
- Product requirements describing project-owned reusable content selection.
- Internal architecture documentation and a new ADR for the durable boundary between project-owned link definitions and in-memory resolved packs.
- Developer reference and how-to documentation for configuring, installing, inspecting, updating, auditing, and removing links.
