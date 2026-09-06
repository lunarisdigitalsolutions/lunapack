---
status: accepted
date: 2026-09-06
decision-makers:
  - Lunaris Engineering
---

# ADR-0083: Evaluate Typed Composite Parameter Expressions

## Context and Problem Statement

Composite references could bind only literal values. Passing a parameter to a
dependency under another name required consumers to provide both values, and a
composite could not derive a dependency choice from its existing conditions.
Treating every string as an expression would break existing literal bindings.

## Decision Drivers

- Reuse the existing condition grammar and parameter type system.
- Preserve all literal string, Boolean, and multi-select bindings.
- Resolve bindings before prompting for hidden transient parameters.
- Reject unknown, cyclic, runtime-dependent, and type-incompatible values
  before project mutation.

## Considered Options

- Keep composite parameter bindings literal-only.
- Treat every string binding as an expression.
- Mark typed expressions explicitly with `${{ expression }}`.

## Decision Outcome

Chosen option: "Mark typed expressions explicitly with `${{ expression }}`",
because an exact marker adds typed evaluation without changing existing string
semantics.

A direct parameter identifier preserves its resolved scalar or multi-select
value. `iif(condition, whenTrue, whenFalse)` evaluates the shared Boolean
condition grammar and returns one compatible typed branch. Expressions can use
only parameters declared by the pack that owns the reference. Lifecycle-only
functions are invalid.

Expression-bound targets remain fixed composite values under existing
precedence. LunaPack defers them until their source values resolve, evaluates
dependency-ready bindings independent of declaration order, and rejects cycles
or values incompatible with the referenced parameter declaration.

### Consequences

- Composite authors can pass through or derive transient dependency values.
- Existing non-expression strings remain literals.
- The shared parser supports Boolean condition mode and typed value mode.
- Expression dependencies add a preflight cycle check to parameter binding.
- Embedded interpolation remains unsupported; Scriban is not used for bindings.

### Confirmation

Parser tests cover direct, conditional, nested, Boolean, scalar, and
multi-select values. Manifest and authoring tests cover syntax, scope,
round-tripping, and atomic rejection. Resolver tests cover pass-through,
conditional selection, target validation, and cycles.

## More Information

This decision extends
[ADR-0021](0021-prioritize-composite-root-parameter-contracts.md) and
[ADR-0072](0072-share-conditions-across-pack-declarations.md).
