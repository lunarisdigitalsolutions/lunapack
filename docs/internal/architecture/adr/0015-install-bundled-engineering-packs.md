---
status: accepted
date: 2026-08-11
decision-makers: Lunaris Digital Solutions
---

# ADR-0015: Install Bundled Engineering Packs in the Repository

## Context and Problem Statement

The repository initially acted only as a source for bundled packs because
version 1 refuses to overwrite managed targets. That left the repository unable
to dogfood its own installation state.

The repository now needs to remove its pre-existing managed targets, install
every first-party pack, and then restore its intentionally repository-specific
content. The generic quality baseline must not distribute dependencies used
only by LunaPack's CLI implementation.

## Decision Drivers

- Record actual first-party pack installations in root `lunapack.yml`.
- Keep generic baseline content broadly reusable.
- Preserve repository-specific Node, documentation, Husky, and CLI needs.
- Make deliberate lifecycle divergence explicit rather than implicit.

## Considered Options

- Keep the repository source-only.
- Expand generic packs until they contain every repository-specific setting.
- Install generic packs and restore repository-specific overlays manually.

## Decision Outcome

Chosen option: "Install generic packs and restore repository-specific overlays
manually", because it makes LunaPack manage its own baseline while preventing the
baseline from becoming a disguised copy of this repository.

### Consequences

- Good, because root `lunapack.yml` records every bundled pack with content
  digests produced by the lifecycle.
- Good, because the quality baseline declares only dependencies directly
  referenced by its `Directory.Build.props` template.
- Good, because the root can retain its Node, documentation, Husky, and CLI
  content without forcing those concerns on all consumers.
- Bad, because manually changed managed files fail future update or uninstall
  checks until the repository reconciles them.

### Confirmation

Verify `lunapack.yml` records all six bundled packs. Verify the root build and
tests after restoring the `.gitignore` and `Directory.Packages.props` overlays.
Verify duplicate installation of an already recorded pack fails without
changing files or the manifest.

## Pros and Cons of the Options

### Keep the Repository Source-Only

- Good, because no root file becomes managed.
- Bad, because the repository does not dogfood installation ownership.

### Expand Generic Packs Until They Contain Every Repository-Specific Setting

- Good, because the root would match every installed digest.
- Bad, because unrelated dependencies and ignore rules become mandatory for
  every consumer.

### Install Generic Packs and Restore Repository-Specific Overlays Manually

- Good, because generic packs remain reusable and root pack state is real.
- Bad, because overlays must be reconciled before lifecycle mutation.

## More Information

See the [pack authoring guide](../../../developer/packs/index.md) and
[source guide](../../../developer/sources.md) for related details.
