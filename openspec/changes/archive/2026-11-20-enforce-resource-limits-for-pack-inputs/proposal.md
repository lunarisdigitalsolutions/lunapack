# Proposal

## Why

Large or deeply nested pack inputs can exhaust memory or processing time before
Luna reports a useful error. Issue [#83](https://github.com/lunarisdigitalsolutions/lunapack/issues/83)
calls for finite, documented ceilings and clear rejection diagnostics across
pack processing, not only existing archive checks.

## What Changes

- Define finite defaults for project and pack manifests, lock files, pack
  documents, rendered content, dependency graph depth and node count, and files
  per pack.
- Expose limit overrides through Luna's existing project, global, and
  user-project-specific configuration scopes.
- Enforce limits before expensive processing or project mutation. Report the
  observed value, configured limit, and unit.
- Align existing archive and template limits with the shared defaults and
  configuration behavior.
- Document the values and overrides; add boundary and failure-preservation
  tests.

## Capabilities

### New Capabilities

- `pack-resource-limits`: Define configurable resource ceilings for pack input
  and lifecycle processing.

### Modified Capabilities

None.

## Impact

- `Lunapack.Cli` configuration, manifest and lock loading, dependency
  resolution, pack-file enumeration, rendering, and archive processing.
- Configuration models and schemas, plus unit, integration, and security tests.
- Public resource-limit guidance, internal compatibility and architecture
  records, and `projects/cli/CHANGELOG.md`.
