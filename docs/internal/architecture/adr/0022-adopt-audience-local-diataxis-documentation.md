---
status: accepted
date: 2026-08-18
decision-makers:
  - Lunaris Engineering
---

# ADR-0022: Adopt Audience-Local Diataxis Documentation

## Context and Problem Statement

Documentation mixed product plans with current CLI guidance, used broad flat
folders, and made it difficult for readers to find an appropriate level of
detail.

## Decision Drivers

- Keep public and maintainer guidance accurate for the current MVP.
- Make product direction explicit without presenting it as shipped behavior.
- Keep navigation small enough to scan.

## Considered Options

- Retain the existing flat documentation structure.
- Organize current guidance by audience and Diataxis purpose.
- Combine all material in one product-oriented documentation tree.

## Decision Outcome

Chosen option: "Organize current guidance by audience and Diataxis purpose,"
because each reader can find current, relevant information without encountering
unimplemented product plans.

### Consequences

- Product documentation owns vision, roadmap, and future capabilities.
- Developer documentation owns the public current CLI and pack contracts.
- Internal documentation owns current implementation and maintainer practice.
- Each audience area keeps its links local and groups sibling pages into small
  navigable sets.
- Existing ADR records retain their immutable chronological archive structure.

### Confirmation

Review documentation changes for audience placement, current-behavior accuracy,
Diataxis purpose, local links, concise writing, and bounded folder size.
