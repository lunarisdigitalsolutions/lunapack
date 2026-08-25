---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0003: Govern Changes with OpenSpec

## Context and Problem Statement

LunaPack is currently defined through product, architecture, and developer
documentation while its runtime components are planned. The repository needs a
change workflow that keeps planned behavior, implementation boundaries, and
documentation updates coherent.

## Decision Drivers

- Keep planned behavior, implementation boundaries, and documentation coherent.
- Define observable behavior before implementation.
- Record documentation impact as part of change planning.

## Considered Options

- Govern changes with OpenSpec.
- Track changes only in product documents.
- Implement capabilities before defining behavior.

## Decision Outcome

Chosen option: "Govern changes with OpenSpec", because it provides a
specification-driven record that connects proposals, design boundaries,
observable behavior, tasks, and documentation impact.

### Consequences

- Good, because planned capabilities have a consistent path from proposal
  through implementation.
- Good, because documentation impact is considered before a change is applied.
- Bad, because OpenSpec artifacts become part of the repository's change record.

### Confirmation

Reviewers confirm that capability changes include the applicable OpenSpec
artifacts and documentation updates before accepting the change.

## Pros and Cons of the Options

### Govern changes with OpenSpec

- Good, because change intent, behavior, and documentation impact remain linked.
- Bad, because contributors must maintain OpenSpec artifacts.

### Track changes only in product documents

- Good, because product-facing plans remain in one location.
- Bad, because design boundaries and tasks would disconnect.

### Implement capabilities before defining behavior

- Good, because implementation can begin immediately.
- Bad, because contracts could become accidental.

## More Information

Use the repository's OpenSpec configuration as the spec-driven change workflow.
Proposals identify affected product, internal, and developer documentation.
Designs preserve the defined domain boundaries. Specifications describe
observable behavior without treating planned capabilities as implemented. Tasks
include documentation updates for user-visible changes.

- [OpenSpec configuration](../../../../openspec/config.yaml)
- [OpenSpec changes](../../../../openspec/changes/)
- [Repository README](../../../../README.md)
