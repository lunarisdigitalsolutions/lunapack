---
status: accepted
decision-makers:
  - Lunaris Engineering
---

# ADR-0089: Separate Direct Pack Roots From Dependency Resolution

## Context and Problem Statement

Catalog packs can be reusable components that should only be installed as part
of a composite pack. Hiding those releases by removing them from the catalog
would also prevent composite resolution, validation, and inspection. The CLI
also needs persistent catalog and guidance defaults that work across a project
and a user's profile without coupling project settings to trust records.

## Decision Drivers

- Let maintainers mark dependency-only releases without changing existing pack
  behavior.
- Keep all catalog releases available for dependency resolution and explicit
  validation or inspection.
- Apply direct-installability rules consistently to install, update,
  outdated, discover, and search roots.
- Preserve scoped preferences, explicit `false` values, and existing trust
  data.
- Keep stored settings compatible with optional, version-1 YAML properties.

## Considered Options

- Remove dependency-only releases from the catalog globally.
- Add a separate catalog category that duplicates dependency relationships.
- Keep the full catalog and mark direct-root eligibility with optional manifest
  metadata.

## Decision Outcome

Chosen option: "Keep the full catalog and mark direct-root eligibility with
optional manifest metadata," because root selection can exclude dependency-only
releases without changing graph resolution or older manifests.

`directInstallable` is optional and defaults to `true`. A pack is composite
when its manifest has one or more `packs` references; otherwise it is raw.
Discover and search include both types by default, can filter to composites,
and always omit releases marked `directInstallable: false`. Install, update,
and outdated select only directly installable releases for requested roots;
dependency edges continue to resolve from the complete catalog. Validation and
inspection also retain complete-catalog access.

Preferences are optional in project and user settings. Their effective value
uses the first explicit setting in project-local, user-project, global, and
built-in order. Built-in composite filters default to `false`; next-step
guidance defaults to `true`. The CLI scope defaults to user-global settings,
and explicit `false` overrides less-specific values. The one-invocation
`--only-composites` and `--suppress-next-steps` options can enable filtering or
disable guidance, respectively.

### Consequences

- Good, because dependency-only packs remain usable by composites and
  discoverable through explicit inspect and validate commands.
- Good, because omitted `directInstallable` metadata preserves eligibility for
  existing packs.
- Good, because project and user preferences share one precedence model while
  user trust data remains intact.
- Bad, because older CLI versions may ignore `directInstallable: false` and
  install a dependency-only release directly.
- Bad, because maintainers must review whether existing packs should become
  dependency-only; this decision does not change current pack metadata.

### Confirmation

Confirm with tests that omitted metadata remains directly installable, hidden
releases are filtered before catalog and lifecycle root selection, composite
dependencies still resolve, inspect and validate retain visibility, and
preference precedence preserves explicit `false` and trust data. Strict
OpenSpec validation checks the related capability contracts.

## Pros and Cons of the Options

### Remove dependency-only releases from the catalog globally

- Good, because every consumer would see only directly installable releases.
- Bad, because composite resolution, validation, and inspection would lose
  access to those releases.

### Add a separate catalog category

- Good, because maintainers could classify component packs outside the
  manifest.
- Bad, because it duplicates pack relationships and adds another source of
  truth.

### Keep the full catalog and mark direct-root eligibility with optional metadata

- Good, because root eligibility is explicit and dependencies keep resolving
  from the complete catalog.
- Bad, because older CLI versions do not enforce the metadata.

## More Information

This decision implements [issue #53](https://github.com/lunarisdigitalsolutions/lunapack/issues/53)
and the OpenSpec change
[`configure-catalog-discovery`](../../../../openspec/changes/configure-catalog-discovery/proposal.md).
It follows [ADR-0034](0034-default-catalog-results-to-latest-release.md) for
catalog result selection and [ADR-0074](0074-evolve-yaml-schemas-compatibly.md)
for optional persisted YAML fields.
