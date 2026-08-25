# Architecture Decision Records

Architecture Decision Records (ADRs) capture accepted, durable decisions that affect LunaPack boundaries, lifecycle semantics, trust, compatibility, governance, repository conventions, or recurring engineering workflows.

## Numbering Convention

- File names use `NNNN-kebab-case-title.md`, starting at `0001`.
- Numbers are sequential across the repository and are never reused.
- Superseded decisions remain in place and link to their replacement.
- Status is one of `proposed`, `rejected`, `accepted`, `deprecated`, or
  `superseded by ADR-NNNN`.

Create new records from the local [MADR-based template](template.md). The
template follows the [MADR ADR template](https://github.com/adr/madr/blob/develop/template/adr-template.md).
