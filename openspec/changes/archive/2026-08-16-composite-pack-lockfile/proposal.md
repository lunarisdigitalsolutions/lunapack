## Why

Composite engineering foundations need to assemble independently published packs
without embedding source locations or duplicating their files. The current
`lunapack.yml` also conflates portable project configuration with machine-specific
resolved source paths and mutable installation ownership state, preventing
reproducible sharing and future synchronization.

## What Changes

- Add composite pack declarations that reference zero or more packs by ID and
  exact Semantic Version. Composite packs may also declare their own managed
  files.
- Resolve composite references recursively from sources configured by the
  consuming project's `lunapack.yml`; pack manifests do not declare sources.
- Reject missing composite references, dependency cycles, and ownership
  conflicts before mutating project files.
- **BREAKING** Make `lunapack.yml` a portable declarative configuration file for
  relative local sources and requested root packs; prohibit absolute source
  paths and move resolved installation state out of this file while retaining
  configuration schema version `1`.
- Introduce versioned `lunapack-lock.yml` for exact resolved pack graph,
  provenance, managed-file ownership, and content digests, establishing the
  state needed for later sync and reconciliation workflows.
- Reject existing combined configuration and resolved-state documents; update
  schema, CLI, product, internal architecture, and developer documentation
  contracts.
- Record the durable configuration/lock-state boundary in a new architecture
  decision record.

## Capabilities

### New Capabilities

- `project-lockfile`: Persist reproducible resolved pack graphs and managed-file
  ownership separately from portable project configuration.

### Modified Capabilities

- `manifest-schemas`: Support composite pack references and separate project
  configuration from lock-file state while enforcing relative local paths.
- `cli-project-configuration`: Initialize, validate, and mutate portable
  configuration plus its corresponding lock file.
- `local-pack-lifecycle`: Resolve and install composite pack graphs using only
  configured sources, then record all resolved state in the lock file.

## Impact

- Affected code: configuration, schema validation, source registration,
  catalog/dependency resolution, installation, uninstallation, auditing, and
  managed-file lifecycle services in `projects/cli`; JSON schemas in
  `projects/schema`.
- Affected files: existing combined-state `lunapack.yml` documents are unsupported
  and newly managed `lunapack-lock.yml` documents become lock state.
- Affected documentation: product requirements, internal pack/source/lifecycle
  architecture, a new ADR and ADR index entry, plus developer configuration,
  manifest, dependency, schema, install, uninstall, audit, and sync guidance.
