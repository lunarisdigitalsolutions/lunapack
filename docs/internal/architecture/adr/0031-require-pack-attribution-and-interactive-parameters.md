---
status: accepted
date: 2026-08-21
decision-makers: LunaPack maintainers
---

# ADR-0031: Require Pack Attribution And Interactive Parameters

## Context and Problem Statement

Pack consumers need attribution before installation, and pack authors need a
clear way to describe values that must be supplied interactively. Failing an
install for an otherwise resolvable required parameter is avoidable friction.

## Decision Outcome

Chosen option: "Require author and license metadata, then prompt for unresolved
required parameters", because it makes pack ownership visible and preserves
explicit command-line, composite-pack, and project-variable precedence.

### Consequences

- `pack.yml` requires non-empty `author` and `license` fields.
- Parameter declarations can include a display name and description.
- `luna install` prompts only after automatic parameter sources are exhausted.
- `luna inspect` presents selected manifest metadata and optional sections.

### Confirmation

Schema, unit, and integration tests validate attribution, parameter resolution,
interactive prompt rendering, inspection output, and versioned validation.

## More Information

- [Pack manifest reference](../../../developer/packs/reference/manifest.md)
- [CLI commands](../../../developer/cli/commands.md)
- [Manifest schema specification](../../../../openspec/specs/manifest-schemas/spec.md)
