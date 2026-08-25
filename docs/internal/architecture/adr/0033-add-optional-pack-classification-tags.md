---
status: accepted
date: 2026-08-21
decision-makers: Lunaris Engineering
---

# ADR-0033: Add Optional Pack Classification Tags

## Context and Problem Statement

Pack IDs and descriptions do not provide consistent catalog classification.
Authors need concise metadata that consumers can search and inspect without
changing existing version-1 pack manifests.

## Decision Drivers

- Preserve compatibility for existing `pack.yml` files.
- Bound metadata to keep catalog output and search predictable.
- Let authors classify packs independently of naming conventions.

## Considered Options

- Continue using IDs and descriptions only.
- Add an optional bounded tag list to pack manifests.
- Add a required hierarchical category field.

## Decision Outcome

Chosen option: "Add an optional bounded tag list to pack manifests", because it
adds searchable classification while preserving existing manifests and avoiding
a fixed taxonomy.

### Consequences

- A pack manifest may declare zero to 15 unique, non-empty tags.
- Catalog search includes tag matches.
- Discover renders all tags; inspect renders the first five.
- Tags remain descriptive metadata and do not affect resolution or lifecycle
  planning.

### Confirmation

Schema tests validate the zero- and 15-tag boundaries. Catalog tests validate
tag-only search. Inspection formatter tests confirm the five-tag preview.

## More Information

The version-1 schema remains backward compatible because `tags` is optional.
