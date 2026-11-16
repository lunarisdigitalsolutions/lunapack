# Proposal

## Why

`luna discover` and `luna search` currently show every cataloged pack without
distinguishing installable profiles from component packs. Consumers need
consistent filters and persistent defaults, while validation, inspection, and
composite dependency resolution must continue to access component packs.

## What Changes

- Add optional `directInstallable` metadata to `pack.yml`, defaulting to `true`
  when omitted. A pack marked `false` is always excluded from discover/search
  and cannot be selected as a directly requested install or update root, but
  remains resolvable for validation, inspection, and composite dependencies.
  Catalog and root install/update selection filter releases before choosing a
  version, so a newer dependency-only release does not hide an older
  directly-installable release.
- Define a composite pack as one that declares one or more pack references;
  packs with no references are raw packs. Discover and search show both kinds
  by default and accept `--only-composites` to show only composite packs.
- Add `luna config set|list|remove` for `discover.only-composites`,
  `search.only-composites`, and `next-steps.enabled`. Scope flags apply to all
  three operations: `--local` uses the current `lunapack.yml`,
  `--user-project` uses the current project's entry in user settings, and
  `--global` uses user-wide settings and is the default scope. Effective values
  resolve in this order: local, user-project, global, built-in defaults.
- Default both `only-composites` settings to `false` and
  `next-steps.enabled` to `true`. The existing `--suppress-next-steps` option
  remains an invocation-level override.
- Review current packs before changing their installability. Of 49 manifests
  (21 with pack references), eight `lint-staged-*` packs may merit owner review
  as profile components, but each may also be useful independently; this
  proposal marks none of them non-installable:
  `lint-staged-csharp`, `lint-staged-css`, `lint-staged-html`,
  `lint-staged-json`, `lint-staged-markdown`,
  `lint-staged-markdownlint`, `lint-staged-scss`, and `lint-staged-yaml`.
  Existing .NET component packs appear independently useful.
- Keep these possible future settings out of scope: `discover.versions`,
  `search.versions`, a persisted draft-visibility preference, and
  `search.include-links` to toggle search's existing link results.

## Capabilities

### New Capabilities

- `cli-preferences`: Manage scoped CLI preference values and use them as
  defaults for catalog filtering and next-step guidance.

### Modified Capabilities

- `manifest-schemas`: Add pack installability metadata and preference values
  to supported project/user configuration.
- `pack-catalog`: Exclude non-direct-installable packs and apply consistent
  raw/composite filtering in discover and search.
- `local-pack-lifecycle`: Reject non-direct-installable packs as requested
  roots while preserving dependency resolution.
- `cli-workflow-guidance`: Honor the persisted next-step preference without
  changing the existing per-run suppression behavior.

## Impact

The change affects the pack and project configuration schemas, pack-manifest
serialization and authoring, catalog filtering, direct-install preflight,
existing user-settings persistence, discover/search command options, and
workflow guidance. Public developer documentation must explain the manifest
property, configuration scopes and precedence, and catalog behavior; internal
documentation must record the accepted compatibility and scope decisions.
