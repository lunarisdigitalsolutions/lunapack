---
status: accepted
date: 2026-11-16
decision-makers:
  - Lunaris Engineering
supersedes: ADR-0086
---

# ADR-0090: Store Source Ignores in a Compatible Sidecar

## Context and Problem Statement

Source-ignore preferences are local to a user and canonical project. Storing
them in `~/.lunapack/config.yml` adds a property that older strict CLI readers
do not recognize, making that existing settings file unreadable after a newer
CLI records an ignored source. User settings have no versioned migration
contract.

## Decision Drivers

- Keep ignore intent local to the user and canonical project.
- Preserve compatibility with older strict readers of existing user settings.
- Preserve identity-bound ignore behavior across source rename.
- Avoid changing shared configuration, lock state, or unrelated trust settings.

## Considered Options

- Add ignore identities to the existing user-settings document.
- Store ignore identities in a separate per-project sidecar.
- Store ignore preferences in shared project configuration.

## Decision Outcome

Chosen option: "Store ignore identities in a separate per-project sidecar,"
because a new CLI can persist its feature state without changing the existing
settings format consumed by older CLI releases.

Persist source-ignore identities in `~/.lunapack/source-ignores.yml`, keyed by
canonical project directory and configured source identity. Keep
`~/.lunapack/config.yml` limited to its existing trust, acknowledgement, and
denial settings. Ignore and unignore operations update only the sidecar;
source listing and access checks read ignore state from it. The sidecar remains
outside the repository and is written atomically with the existing user-local
file security protections.

## Consequences

- Older strict CLI readers can continue loading `config.yml` after a newer CLI
  records source-ignore state.
- Ignore state is isolated from shared project files and unrelated user trust.
- The sidecar has its own read, validation, and write failure modes; failures
  must be surfaced rather than treated as an empty ignore list.
- Future incompatible sidecar changes require an explicit version/migration
  strategy.

## Confirmation

Tests verify ignore/unignore persistence, canonical-project isolation,
atomic/idempotent updates, and that saving ignore state leaves `config.yml`
readable by a strict legacy reader without adding new settings properties.

## More Information

See [ADR-0086](0086-store-source-ignores-in-user-settings.md),
[ADR-0074](0074-evolve-yaml-schemas-compatibly.md), and the
`add-oci-registry-sources-and-publishing` OpenSpec change.
