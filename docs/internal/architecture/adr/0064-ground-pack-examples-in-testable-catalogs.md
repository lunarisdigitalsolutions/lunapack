---
status: accepted
date: 2026-08-29
decision-makers:
  - Lunaris Engineering
---

# ADR-0064: Ground Pack Examples in Testable Catalogs

## Context and Problem Statement

Public documentation used plausible pack IDs and versions that were absent from
`projects/packs`. Readers could copy valid Luna commands and still receive a
pack-not-found or version-not-found error unrelated to the behavior being
explained. Pack-authoring guidance also reused generic IDs without consistently
showing where those packs were created and registered.

## Decision Drivers

- Keep consumer commands directly testable against maintained repository packs.
- Prevent example failures caused by fictional releases.
- Keep pack-authoring exercises isolated from maintained pack identities.
- Make creation, source registration, validation, installation, and update order
  explicit in authoring workflows.

## Considered Options

- Allow arbitrary illustrative pack references everywhere.
- Use maintained packs in every example, including authoring exercises.
- Separate maintained consumer examples from newly created authoring examples.

## Decision Outcome

Chosen option: "Separate maintained consumer examples from newly created
authoring examples," because it keeps consumer commands executable while letting
pack authors create and mutate disposable packs without colliding with repository
content.

### Consequences

- Consumer commands reference only pack IDs present under `projects/packs`, and
  every explicit version identifies a maintained release there.
- Pack-authoring examples use clearly synthetic `example-*` IDs absent from
  `projects/packs`.
- An authoring workflow initializes a synthetic pack before using it and
  registers its catalog before running consumer commands against it.
- Versioning examples create or copy every referenced release before validation,
  installation, or update.
- Adding, removing, or versioning maintained packs can require documentation
  example updates.

### Confirmation

Documentation review compares executable consumer pack references with
`projects/packs/**/pack.yml`. Review also confirms every synthetic reference is
inside an authoring workflow, follows creation, and uses only versions created by
that workflow. Markdown lint and the developer-site production build validate
the resulting pages and links.
