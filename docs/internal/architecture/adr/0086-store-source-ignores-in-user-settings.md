---
status: accepted
date: 2026-11-08
decision-makers:
  - Lunaris Engineering
---

# ADR-0086: Store Source Ignores in User Settings

## Context and Problem Statement

Consumers need to disable an unavailable or unwanted source in one local
workspace without changing shared project configuration, lock ownership, or
other users' behavior.

## Decision Drivers

- Keep ignore intent local to the person and canonical project.
- Preserve configured-source order and visibility.
- Avoid network, filesystem, credential, or prompt access for ignored sources.
- Keep existing trust and installed ownership evidence intact.

## Considered Options

- Remove the source from `lunapack.yml`.
- Add a shared project ignore flag.
- Store source-ignore identities in the existing per-project user settings.

## Decision Outcome

Chosen option: "Store source-ignore identities in per-project user settings,"
because source availability and user preference are local decisions, not
portable project intent.

Ignore and unignore commands resolve the configured source identity and update
user settings for the canonical project directory. Ignored sources remain
visible in configured order with an `ignored locally` status. Catalog browsing,
direct resolution, links, external content, authentication, and publishing
must gate the source before access. Rename preserves the identity-bound ignore
state; removing and re-adding a source with a changed identity does not silently
inherit it.

## Consequences

- Ignore state is absent from `lunapack.yml`, lock files, and schemas.
- Ignoring a source does not revoke trust or remove installed pack evidence.
- Required locked operations fail with unignore guidance instead of silently
  selecting another source.
- User settings remain machine-local and are not shared by collaborators.

## Confirmation

Tests verify canonical-project isolation, atomic and idempotent writes,
configured-order listing, rename behavior, no-access gating, and required-source
conflict diagnostics.

## More Information

See the `add-oci-registry-sources-and-publishing` OpenSpec change and
`docs/developer/sources.md`.
