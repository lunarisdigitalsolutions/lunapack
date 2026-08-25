---
status: accepted
date: 2026-08-18
decision-makers: [Lunaris Digital Solutions]
---

# ADR-0021: Prioritize composite root parameter contracts

## Context and Problem Statement

Composite packs can layer parameter declarations across root and transient
packs. Consumers need the root contract to control exposed parameter shape, and
authors need a way to fix transient values without prompting consumers.

## Decision Drivers

- Keep composite parameter contracts predictable for consumers.
- Preserve type safety across the resolved graph.
- Allow composite authors to hide implementation-only transient inputs.

## Considered Options

- Use root-nearest declarations and scalar reference bindings.
- Require identical declarations across every graph node.
- Merge compatible enum values across declarations.

## Decision Outcome

Chosen option: "Use root-nearest declarations and scalar reference bindings",
because it makes the installed composite own its consumer-facing contract while
preserving type validation and explicit transient binding.

### Consequences

- Good, because a root can narrow requiredness and enum values predictably.
- Good, because transient values can remain unavailable to consumer overrides.
- Bad, because composite authors must keep same-name parameter types aligned.

### Confirmation

Resolver, schema, lifecycle, and audit formatter tests verify the contract.

## Pros and Cons of the Options

### Use root-nearest declarations and scalar reference bindings

- Good, because it provides clear precedence and controlled encapsulation.
- Bad, because it expands composite reference syntax.

### Require identical declarations across every graph node

- Good, because it minimizes precedence rules.
- Bad, because roots cannot tailor their public parameter contract.

### Merge compatible enum values across declarations

- Good, because it preserves every declared option.
- Bad, because transient options can unintentionally become public.

## More Information

Reference bindings accept only string and boolean values in version-1 manifests.
