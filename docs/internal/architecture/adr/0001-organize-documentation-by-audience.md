---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0001: Organize Documentation by Audience

## Context and Problem Statement

LunaPack has product direction, internal architecture guidance, developer-facing
CLI and pack documentation, and stakeholder pitch material. Mixing these
audiences in one area would obscure ownership and make it easier to publish
maintainer detail as public guidance.

## Decision Drivers

- Let each documentation reader quickly find guidance intended for them.
- Keep maintainer-only detail separate from developer and stakeholder material.
- Make documentation ownership explicit.

## Considered Options

- Organize documentation by audience.
- Organize documentation by subsystem.
- Copy shared guidance into each audience area.

## Decision Outcome

Chosen option: "Organize documentation by audience", because it makes reader
intent and publication boundaries clear while retaining a dedicated home for
each documentation type.

### Consequences

- Good, because readers can start from an audience-specific documentation index.
- Good, because changes require an explicit decision about their intended readers.
- Bad, because closely related facts can need coordinated updates in more than
  one area.

### Confirmation

Maintainers verify during documentation review that each new document has a
primary audience and belongs under the matching documentation index.

## Pros and Cons of the Options

### Organize documentation by audience

- Good, because reader intent and publication boundaries are explicit.
- Bad, because related guidance can span several audience areas.

### Organize documentation by subsystem

- Good, because material about a subsystem would be colocated.
- Bad, because reader intent would be less clear.

### Copy shared guidance into each audience area

- Good, because each area would appear self-contained.
- Bad, because duplicated content would drift.

## More Information

- [Repository README](../../../../README.md)
- [Documentation index](../../../index.md)
- [OpenSpec configuration](../../../../openspec/config.yaml)
