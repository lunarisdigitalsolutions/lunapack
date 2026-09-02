---
status: accepted
date: 2026-09-01
decision-makers: LunaPack maintainers
---

# ADR-0076: Prompt Parameters Across Active Composite Paths

## Context and Problem Statement

ADR-0075 selected conditional references after resolving every graph parameter.
That sequence asked users for parameters owned only by branches their earlier
answers would disable. Conditional requiredness also needs resolved prior input
without making prompt order implicit.

## Decision Drivers

- Preserve manifest declaration order as visible prompt order.
- Avoid asking for parameters that cannot affect the selected lifecycle.
- Keep shared dependencies when any active path reaches them.
- Reuse the condition grammar, including membership and `isDefault`.
- Reject circular or forward prompt dependencies at manifest validation.

## Considered Options

- Resolve and prompt every candidate-graph parameter before selection.
- Select each branch once from parameter defaults.
- Interleave parameter prompting with active-path traversal.

## Decision Outcome

Chosen option: "Interleave parameter prompting with active-path traversal,"
because each answer can immediately remove unreachable work while preserving
another active route to a shared pack.

Luna prompts each root in request order. Within a pack, it prompts parameters in
manifest order before evaluating outgoing references in manifest order. It then
walks selected references depth-first. Every new answer restarts this traversal
with the accumulated values, so branch conditions use current answers. A pack
is omitted only when no active path reaches it.

A parameter may declare `requiredWhen` instead of `required`. The expression
uses the shared condition grammar and may reference only parameters declared
earlier in that pack. This restriction makes conditional requiredness
deterministic before its prompt position.

### Consequences

- Inactive branches do not produce irrelevant parameter prompts.
- Shared packs remain promptable through any active incoming path.
- Parameter mapping order is a behavioral contract for pack authors.
- Candidate releases still resolve before prompting for version and declaration
  validation; only prompting and active selection are interleaved.
- ADR-0075 is superseded because its all-parameters-before-selection sequence no
  longer describes lifecycle behavior.

### Confirmation

Schema and model tests reject ambiguous or forward `requiredWhen` declarations.
Resolver tests assert declaration order, inactive-branch pruning, and alternate
active paths to a shared pack. Install and update command tests verify staged
answers are applied before lifecycle planning.
