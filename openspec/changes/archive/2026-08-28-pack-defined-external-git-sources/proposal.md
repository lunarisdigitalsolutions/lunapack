# Pack-Defined External Git Sources Proposal

## Why

Packs can distribute only content stored beside their own manifest, forcing authors to copy files from upstream Git repositories and manually keep those copies current. LunaPack needs a declarative, consent-based way to consume external Git content while preserving reproducible resolution, pack ownership, lifecycle safety, and provenance.

## What Changes

- Allow `pack.yml` to declare pack-local aliases for external Git sources with an explicit ref and optional base path and description; pack-defined local sources and externally sourced lifecycle scripts remain invalid.
- Allow managed file, directory, and glob selectors to read declarative content from a declared external source while retaining the pack as owner and enforcing source-root and workspace-root path containment.
- Normalize Git repository identities, refs, and base paths into source fingerprints; require unique fingerprints in `lunapack.yml` and reuse an existing workspace source identifier for equivalent pack requirements.
- Resolve and deduplicate used external-source requirements across the complete pack dependency graph before install or update mutation; ignore unused declarations except for authoring validation warnings.
- Require all-or-nothing approval before adding missing sources, support `--accept-sources` only when proposed identifiers are conflict-free, and resolve identifier conflicts interactively without replacing configured sources.
- Extend source commands with GitHub shorthand, fingerprint-aware duplicate detection, safe rename and removal checks, while retaining existing workspace support for local sources and Git sources whose ref is omitted.
- Make install and update transactional across source configuration, managed files, and lock state; surface source mappings and planned source additions in dry runs.
- Record pack aliases, authoritative workspace source identifiers, fingerprints, canonical requested refs, resolved commits, and per-file provenance in `lunapack-lock.yml`; detect source configuration drift in update and audit flows.
- Extend update and outdated behavior to account for changed external Git content and glob membership, retain unused configured sources, and keep uninstall ownership behavior unchanged while permitting optional cleanup guidance.
- Add bounded next-step guidance to the affected authoring and lifecycle commands.
- Update product requirements, internal architecture and security guidance, and developer-facing pack authoring, source, install, update, audit, and troubleshooting documentation.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `manifest-schemas`: Allow and validate external Git source declarations, source-aware selectors, and external-source provenance in versioned lock state.
- `git-pack-sources`: Define canonical repository, ref, and path identities, source fingerprints, shared cache behavior, and safe external content materialization.
- `cli-project-configuration`: Enforce workspace source uniqueness by fingerprint and add GitHub shorthand, source rename, and consumer-aware removal behavior.
- `pack-authoring`: Add commands to create, inspect, validate, reference, and remove pack-defined external Git sources.
- `local-pack-lifecycle`: Resolve dependency-graph source requirements, obtain approval, plan and apply external files atomically, detect external updates and drift, audit provenance, and preserve ownership during uninstall.
- `project-lockfile`: Persist authoritative external-source mappings, immutable revisions, consumer relationships, and per-file provenance.
- `cli-workflow-guidance`: Provide bounded next steps for external-source authoring, approval, reuse, failure, install, update, and uninstall outcomes.

## Impact

- **Public contracts:** `pack.yml`, `lunapack.yml`, and `lunapack-lock.yml` schemas and their compatibility rules change. Existing pack manifests, workspace-local sources, workspace Git sources without explicit refs, and lock records remain readable; successful lifecycle mutations write the current lock shape.
- **CLI:** Pack authoring, source management, install, update, outdated, dry-run, audit, and uninstall commands gain external-source behavior and diagnostics.
- **Core implementation:** Pack parsing, dependency resolution, source identity normalization, Git resolution and caching, file selection, ownership planning, transaction rollback, and drift detection are affected.
- **Security:** Source declarations remain untrusted, credentials are rejected or sanitized, refs and paths are canonicalized, external content is never executed, and source consent remains separate from script trust.
- **Documentation:** Product requirements under `docs/product`, maintainer architecture and security guidance under `docs/internal`, and public pack/source/lifecycle guidance under `docs/developer` require coordinated updates. A new ADR will record source fingerprint authority, alias mapping, and transactional approval boundaries.
