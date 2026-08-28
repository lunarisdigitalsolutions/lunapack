## Why

Managed files can be remapped by consumers, so a template that hardcodes another
managed file's declared target can render a broken reference. Pack authors need
a constrained way to resolve planned effective targets without exposing the
consumer's filesystem to Scriban.

## What Changes

- Expose `files.path(target)` to managed-file Scriban templates to resolve a
  manifest-declared target to its effective target in the resolved installation
  plan.
- Expose `files.relative_path(target)` to calculate a relative path from the
  current template file's effective target directory to the referenced file's
  effective target.
- Normalize all returned paths to `/` separators on every platform.
- Warn and return the original declared target when no selected managed file
  resolves the reference, including when its condition excludes it.
- Use identical resolution behavior during installation, update, and dry-run
  planning without granting templates filesystem access.
- Document the Scriban functions for pack authors, their warning fallback, and
  the resolved-plan trust boundary; record the durable template API decision in
  an ADR and announce the new externally observable capability in the changelog.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `pack-template-rendering`: Managed-file templates can resolve declared managed
  targets to effective project-relative or relative rendered paths from
  the resolved installation plan, with portable separators and warning-based
  fallback for unavailable targets.

## Impact

- Managed-file lifecycle planning and Scriban rendering context, including the
  effective target map and current template target supplied to the renderer.
- Warning diagnostics for unresolved or conditionally excluded managed targets.
- Focused renderer and lifecycle tests covering remapping, relative paths,
  fallback warnings, platform-independent separators, and install/update/dry-run
  parity.
- Pack-author documentation under `docs/developer`, maintainer architecture and
  path-handling documentation under `docs/internal`, product requirements under
  `docs/product`, ADR-0057, and `CHANGELOG.md`.
- No pack-manifest or project-state schema changes and no new filesystem access
  from Scriban.
