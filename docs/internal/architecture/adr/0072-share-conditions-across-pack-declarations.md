---
status: accepted
date: 2026-09-01
decision-makers: LunaPack maintainers
---

# ADR-0072: Share Conditions Across Pack Declarations

## Context and Problem Statement

Managed files can be selected from resolved pack parameters, but lifecycle
hooks previously ran unconditionally. Packs therefore used required inputs and
always displayed follow-up instructions even when a consumer had already
customized an optional value.

## Decision Drivers

- Use one expression contract for file and lifecycle planning.
- Let packs distinguish a usable default from a consumer override.
- Omit irrelevant instructions before loading their content.
- Omit irrelevant scripts before trust authorization.

## Considered Options

- Keep conditions limited to managed files.
- Add a separate hook-only expression language.
- Share the existing condition grammar and add a default predicate.

## Decision Outcome

Chosen option: "Share the existing condition grammar and add a default
predicate," because pack authors need one predictable selection model across
managed files, scripts, and instructions.

Managed-file selectors and lifecycle hooks accept the same optional `condition`.
The `isDefault(parameterName)` predicate compares the resolved value with an
explicitly declared parameter default. A default predicate for a parameter
without a default is invalid. False hook conditions are removed during planning,
before instruction loading or script authorization.

### Consequences

- Pack authors can show customization guidance only while a default remains.
- Hook conditions compose with existing Boolean, comparison, membership, and
  logical operators.
- Required parameters without defaults cannot use `isDefault`.
- Existing manifests remain valid because `condition` is optional.

### Confirmation

Parser tests cover scalar, Boolean, multi-select, negated, and missing-default
predicates. Lifecycle planner tests confirm false hooks are omitted, and schema
contract tests require `condition` on both hook variants.
