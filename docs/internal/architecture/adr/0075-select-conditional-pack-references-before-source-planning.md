---
status: superseded by ADR-0076
date: 2026-09-01
decision-makers: LunaPack maintainers
---

# ADR-0075: Select Conditional Pack References Before Source Planning

## Context and Problem Statement

Composite packs always resolved every referenced release into lifecycle planning.
A pack could condition individual managed files and hooks, but could not omit an
entire optional capability. Referenced packs with external Git content therefore
required source approval and materialization even when the consumer did not need
them.

Dry runs also applied optional parameter defaults without asking the consumer,
which hid configurable branches from the preview.

## Decision Drivers

- Reuse the parameter expression contract established by ADR-0072.
- Omit unused referenced packs from files, hooks, ownership, and source planning.
- Let dry runs show the direct impact of configurable parameter choices.
- Preserve compatibility for manifests without conditional references.

## Considered Options

- Keep composite references unconditional.
- Condition only external-source requirements.
- Select the active graph from resolved parameters before lifecycle planning.

## Decision Outcome

Chosen option: "Select the active graph from resolved parameters before lifecycle
planning," because one graph decision must consistently govern every downstream
effect.

A composite reference accepts an optional `condition` using the shared parameter
expression grammar. Luna resolves parameter declarations and values, evaluates
reference conditions, and passes only reachable packs into external-source,
managed-file, hook, ownership, and lock planning. An unselected reference does
not require its pack-defined external source.

Install and update dry runs prompt for every consumer-configurable parameter
before planning. Real operations retain required-only prompting unless the user
passes `--prompt-parameters`, which prompts optional parameters and offers their
declared defaults.

### Consequences

- Optional composite capabilities no longer add unused source consent or lock
  evidence.
- Dry runs are interactive when a graph declares configurable parameters.
- `--prompt-parameters` provides the same complete configuration pass for real
  install and update operations.
- Candidate releases still resolve before condition selection so Luna can
  validate graph-wide parameter declarations and exact reference identities.

### Confirmation

Schema and authoring tests cover reference conditions. Graph-selection and
external-source planner tests confirm false references and their sources are
omitted. Parameter resolver and command tests cover optional prompt metadata and
update behavior.
