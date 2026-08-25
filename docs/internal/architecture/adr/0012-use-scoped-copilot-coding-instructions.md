---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0012: Use Scoped Copilot Coding Instructions

## Context and Problem Statement

LunaPack has C# conventions, schema compatibility commitments, .NET build rules,
and OpenSpec change governance. A single repository-wide Copilot instruction
cannot provide relevant guidance at the point of change without burdening every
task with unrelated detail.

## Decision Drivers

- Provide change-specific repository guidance to Copilot.
- Keep the authoritative conventions in maintainer documentation.
- Avoid duplicating or conflicting rules across instruction files.
- Make the workflow versioned and reviewable with the repository.

## Considered Options

- Use scoped Copilot instruction files that link to maintainer references.
- Keep all Copilot guidance in one repository-wide instruction file.
- Rely on contributors to find applicable documentation manually.

## Decision Outcome

Chosen option: "Use scoped Copilot instruction files that link to maintainer
references", because it loads guidance only for relevant paths while keeping
the detailed rules in one maintained location.

### Consequences

- Good, because C#, test, project, schema, pack-manifest, and OpenSpec changes
  receive the relevant conventions automatically.
- Good, because detailed rules remain in
  [coding guidelines](../../development/coding-guidelines/index.md) instead of
  being copied into every instruction file.
- Bad, because maintainers must update instruction links when documentation or
  path ownership changes.

### Confirmation

Verify that instruction `applyTo` patterns cover their intended repository
paths, linked documentation resolves, and the relevant formatters, tests, and
OpenSpec validation pass.

## Pros and Cons of the Options

### Use Scoped Copilot Instruction Files That Link to Maintainer References

- Good, because guidance is contextual and reviewable.
- Bad, because multiple instruction files require ongoing ownership.

### Keep All Copilot Guidance in One Repository-Wide Instruction File

- Good, because there is one file to maintain.
- Bad, because unrelated guidance is loaded for most changes.

### Rely on Contributors to Find Applicable Documentation Manually

- Good, because no instruction files are needed.
- Bad, because conventions are easy to overlook during focused changes.

## More Information

See [C# coding guidelines](../../development/coding-guidelines/csharp.md),
[JSON Schema coding guidelines](../../development/coding-guidelines/json-schema.md),
and the [Copilot instruction directory](../../../../.github/instructions/).
