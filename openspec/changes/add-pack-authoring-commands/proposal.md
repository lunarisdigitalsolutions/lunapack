## Why

Pack authors must create and maintain `pack.yml` by hand, which makes simple
authoring workflows depend on detailed schema knowledge. First-class commands
should make valid manifests incrementally authorable while preserving YAML as an
editable public contract.

## What Changes

- Add a `luna pack` command group for initializing, inspecting, validating, and
  incrementally editing a pack manifest.
- Add commands for managed files, directories, globs, composite references,
  lifecycle scripts, parameters, tags, and pack metadata so every supported
  manifest concept has a CLI authoring path.
- Prompt for missing required initialization values in interactive terminals and
  require them as options when interaction is unavailable.
- Validate the complete candidate manifest before each atomic write and leave the
  existing file unchanged when validation fails.
- Require author and license metadata for a newly initialized manifest while
  allowing empty content collections for incremental authoring.
- Extend optional pack metadata with a human-readable name and homepage.
- Keep command names consistent with existing groups: `list`, `set`, `rm`, and
  noun-specific `add` subcommands.

## Capabilities

### New Capabilities

- `pack-authoring`: Create, inspect, validate, and safely mutate local pack
  manifests through the CLI.

### Modified Capabilities

- `manifest-schemas`: Require non-empty author and license metadata while
  defining optional name and homepage metadata.

## Impact

- Affected code: CLI composition, pack-manifest loading and serialization,
  interactive input, validation, path handling, and atomic persistence.
- Affected contracts: `pack.yml` schema and generated YAML shape; existing valid
  manifests remain valid.
- Affected tests: command parsing, interactive and redirected input, every
  manifest mutation, path normalization, validation failures, duplicate
  handling, and write atomicity.
- Affected product documentation: CLI-first pack-authoring requirements and
  supported author journey.
- Affected internal documentation: authoring-service boundaries, validation and
  persistence ownership, path handling, and an accepted ADR for incremental
  manifest validity.
- Affected developer documentation: pack-authoring tutorial, command reference,
  manifest metadata, managed content, parameters, references, and lifecycle
  scripts.
- Affected changelog: new authoring commands and relaxed pack-manifest minimum.
