---
status: accepted
date: 2026-08-31
decision-makers:
  - Lunaris Engineering
---

# ADR-0066: Organize Maintained Packs by Family and Role

## Context and Problem Statement

ADR-0065 placed every maintained pack directly below `projects/packs` using
`<pack-id>/<version>`. That release layout preserved immutable versions, but a
flat source root became difficult to scan as the catalog grew across tools,
platforms, ecosystems, and repository concerns.

Catalog discovery already searches recursively and treats each manifest's
directory as its release root. Parent directories therefore can organize the
maintained portfolio without changing pack IDs, dependency references, source
configuration, or manifest semantics.

## Decision Drivers

- Keep related pack families together as the maintained catalog grows.
- Preserve stable pack IDs and immutable version directories.
- Make primitives, integrations, profiles, and foundations easier to inspect.
- Avoid making subjective pack taxonomy part of the manifest contract.
- Keep one source root usable for discovery and dependency resolution.

## Considered Options

- Retain a flat list of pack IDs below the source root.
- Group packs only by ecosystem or platform.
- Group packs only by primitive, adapter, profile, or foundation role.
- Group first by stable family and then by role or concern.

## Decision Outcome

Chosen option: "Group first by stable family and then by role or concern,"
because stable families provide predictable browsing while a secondary role
distinguishes components, integrations, profiles, and foundations where useful.

### Consequences

- Maintained releases live at
  `<family-path>/<role-or-concern>/<pack-id>/<version>/pack.yml` below
  `projects/packs`.
- Top-level families are `ecosystems`, `platforms`, `tooling`, `repository`,
  and `lunapack`.
- Ecosystem and platform families may add a named ecosystem or platform before
  the role. Tool families may add a canonical tool name before the role.
- Cross-family packs live with their primary discoverable owner. Tags and
  manifest dependencies expose secondary relationships.
- Classification directories remain repository organization only. Pack IDs,
  versions, manifests, tags, and dependencies remain catalog authority.
- Pack content retains ADR-0065's version and payload-purpose layout.
- A new family or role requires demonstrated catalog need; empty categories
  are not added prospectively.

### Confirmation

Repository review confirms each first-level directory below `projects/packs`
is an approved family, every pack ID directory appears below an appropriate
role or concern, and every release retains `<pack-id>/<version>/pack.yml`.
Catalog tests confirm recursive discovery. Pack validation confirms moved
release roots resolve all local payloads and dependencies.

## More Information

This decision supersedes ADR-0065's direct
`projects/packs/<pack-id>/<version>` placement. ADR-0065's immutable release
roots and payload-purpose directories remain in force.
