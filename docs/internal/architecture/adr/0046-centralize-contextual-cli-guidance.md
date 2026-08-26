---
status: accepted
date: 2026-08-25
decision-makers: [Lunaris Digital Solutions]
---

# ADR-0046: Centralize Contextual CLI Guidance

## Context and Problem Statement

Command handlers report outcomes but cannot consistently infer the next useful
workflow action. Luna needs workspace-aware guidance without moving catalog or
lifecycle policy into console presentation.

## Decision Drivers

- Keep recommendations consistent across commands.
- Derive workspace maturity from validated portable project state.
- Preserve command exit codes, dry-run semantics, and domain boundaries.
- Bound and safely render dynamic command text.

## Considered Options

- Centralize stage classification and recommendation selection.
- Embed recommendation strings in each command handler.
- Add workflow text to generated command help.

## Decision Outcome

Chosen option: "Centralize stage classification and recommendation selection",
because one advisor can own ordering and limits while handlers retain outcome
context and one renderer owns presentation.

`INextStepAdvisor` classifies valid workspaces from configured sources and
requested root packs. It combines that stage with typed command contexts and
returns at most three ordered recommendation values. `NextStepRenderer` escapes
and renders those values. Neither component executes commands or mutates state.

### Consequences

- Good, because workspace and command guidance share one deterministic contract.
- Good, because dry runs and parse failures remain free of misleading guidance.
- Good, because dynamic pack IDs pass through the existing console escaping
  boundary.
- Bad, because command handlers gain explicit guidance collaborators.

### Confirmation

Advisor, renderer, handler, and process tests verify maturity stages, ordering,
limits, escaping, root invocation, workflow transitions, recovery output, and
dry-run suppression.

## Pros and Cons of the Options

### Centralize stage classification and recommendation selection

- Good, because selection can change independently from rendering.
- Bad, because application composition adds another internal service.

### Embed recommendation strings in each command handler

- Good, because each handler has direct outcome context.
- Bad, because formatting, ordering, and limits would diverge.

### Add workflow text to generated command help

- Good, because no new root action is required.
- Bad, because static help cannot inspect workspace state.

## More Information

- [Runtime contracts](../runtime.md)
- [Command reference](../../../developer/cli/commands.md)
- [Contextual guidance OpenSpec change](../../../../openspec/changes/add-contextual-next-step-guidance/)
