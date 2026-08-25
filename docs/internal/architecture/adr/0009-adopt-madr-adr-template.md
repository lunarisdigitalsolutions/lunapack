---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0009: Adopt the MADR ADR Template

## Context and Problem Statement

Existing ADRs used inconsistent metadata and section structures, making records
harder to scan and compare. The repository needs a maintained, established
template that captures decision context, alternatives, outcomes, and validation.

## Decision Drivers

- Apply one consistent structure to every architecture decision record.
- Capture the reasoning behind accepted decisions and their alternatives.
- Use a maintained, established ADR convention rather than a local invention.

## Considered Options

- Adopt the MADR ADR template.
- Keep the existing local ADR template.
- Design a new repository-specific ADR template.

## Decision Outcome

Chosen option: "Adopt the MADR ADR template", because it provides an
established metadata and decision-rationale structure that fits the repository's
architecture governance needs.

### Consequences

- Good, because ADRs consistently expose status, context, drivers, options,
  outcome, consequences, and confirmation.
- Good, because maintainers can compare decisions using a familiar structure.
- Bad, because existing records require migration and future records must follow
  the MADR structure.

### Confirmation

The local template and every ADR in this directory use MADR metadata and the
MADR decision-rationale sections. The `lint:docs` command validates their
Markdown formatting.

## Pros and Cons of the Options

### Adopt the MADR ADR template

- Good, because it captures decision rationale consistently.
- Good, because it is a maintained, established convention.
- Bad, because optional MADR sections can make short decisions more detailed.

### Keep the existing local ADR template

- Good, because it requires no migration.
- Bad, because records would retain inconsistent structure and limited rationale.

### Design a new repository-specific ADR template

- Good, because it could be tailored narrowly to current repository practices.
- Bad, because it would recreate an established convention without its support.

## More Information

- [MADR ADR template](https://github.com/adr/madr/blob/develop/template/adr-template.md)
- [Local ADR template](template.md)
