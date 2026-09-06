---
status: accepted
date: 2026-09-04
decision-makers:
  - Lunaris Engineering
---

# ADR-0081: Inherit Composite Remaps and Evaluate Runtime Hook State

## Context and Problem Statement

Composite packs could bind parameters and suppress lifecycle events, but could
not adapt dependency targets to the layout promised by the composite. Hook
conditions were evaluated entirely during planning, so instructions could not
respond to invocation-wide script suppression or the result of an earlier
script.

## Decision Drivers

- Let a composite define portable defaults for its complete dependency subtree.
- Preserve consumer control over project layout.
- Keep script authorization complete before any hook is processed.
- Preserve ordered hook behavior and rollback on script failure or cancellation.

## Considered Options

- Keep remapping consumer-only and hook conditions planning-only.
- Apply composite remaps only to direct dependencies and authorize scripts lazily.
- Inherit remaps through active graph paths and evaluate runtime hook functions during dispatch.

## Decision Outcome

Chosen option: "Inherit remaps through active graph paths and evaluate runtime
hook functions during dispatch," because it supports reusable composites and
fallback instructions without weakening consumer precedence or script trust.

A remap on a composite reference applies to that referenced pack and its active
dependency subtree. The nearest matching reference wins. Equal-depth paths may
agree; conflicting effective targets fail planning. Command input, requested
pack configuration, top-level project configuration, and locked ownership all
remain above composite defaults.

Lifecycle conditions retain parameter-only planning behavior. Conditions using
`scriptsSkipped()` or `previousScriptState()` remain in declaration order until
dispatch. LunaPack still resolves and authorizes every script that could execute
before processing any hook. A statically false script is not authorized, but
records `ignored` for a following condition.

### Consequences

- Composite authors can establish dependency layouts while consumers retain final control.
- Active graph path provenance becomes part of managed-target planning.
- Hook dispatch tracks the nearest earlier script per pack and lifecycle event.
- Global skip and policy denial are distinguishable from individual decline.
- Dry runs label previous-state conditions as runtime-dependent instead of inventing outcomes.

### Confirmation

Schema and model tests validate remap paths and expression contexts. Graph and
planner tests cover nesting, inactive paths, equivalent and conflicting diamonds,
precedence, and provenance. Lifecycle tests cover ignored and globally skipped
scripts, authorization, dispatch ordering, and dry-run labels.

## More Information

This decision extends [ADR-0053](0053-unify-ordered-lifecycle-hooks.md),
[ADR-0059](0059-treat-ignore-remaps-as-unowned-exclusions.md), and
[ADR-0072](0072-share-conditions-across-pack-declarations.md).
