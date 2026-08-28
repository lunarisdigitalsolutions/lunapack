---
status: accepted
date: 2026-08-28
decision-makers: LunaPack maintainers
---

# ADR-0058: Represent Multi-Select Enums As Ordered Arrays

## Context and Problem Statement

Pack authors need one parameter to select any number of values from a declared
enum. CLI input, YAML defaults, project variables, composite bindings,
conditions, and Scriban must share one representation without weakening the
restricted managed-file condition language or Native AOT serialization.

## Decision Drivers

- Preserve selection order across every input and rendering boundary.
- Reject ambiguous duplicates and values outside the controlling declaration.
- Preserve scalar enum behavior for existing version-1 manifests.
- Fail invalid values before project files or state change.

## Considered Options

- Ordered unique string arrays with graph-aware validation.
- Unordered sets.
- Delimited scalar strings.

## Decision Outcome

Chosen option: "Ordered unique string arrays with graph-aware validation",
because YAML and Scriban expose sequences, repeated CLI assignments naturally
preserve order, and graph resolution owns the controlling enum declaration.

Only enums may set `multiple: true`. Defaults, project variables, and composite
bindings use string arrays. Generic YAML boundaries validate array structure;
the parameter resolver validates declaration shape and allowed values. Duplicate
values fail instead of being deduplicated. An unresolved optional multi-select
enum resolves to an empty array.

Managed-file conditions use the constrained literal-left form `"docker" in
features`. Scriban receives a constrained array adapter supporting `features
contains "docker"`. Both evaluate ordinal membership. Existing scalar equality,
strict variables, and template trust boundaries remain unchanged.

### Consequences

- Good, because all consumers observe one deterministic selection order.
- Good, because invalid arrays fail before lifecycle mutation.
- Good, because omitted `multiple` retains scalar enum compatibility.
- Bad, because static YAML serialization needs explicit scalar-or-array
  converters at object-valued boundaries.
- Bad, because Scriban's default parser needs a constrained callable-array
  adapter for the requested infix-looking syntax.

### Confirmation

Schema, YAML store, resolver, prompt, condition parser, renderer, lifecycle,
transaction, and pack-authoring tests cover valid arrays, ordering, empty
selection, incompatible shapes, duplicate and unknown values, and rollback.

## Pros and Cons of the Options

### Ordered Unique String Arrays With Graph-Aware Validation

- Good, because order survives CLI, YAML, prompts, and rendering.
- Bad, because generic binding schemas cannot validate target enum membership.

### Unordered Sets

- Good, because uniqueness is intrinsic.
- Bad, because no persisted set contract exists and output order becomes unclear.

### Delimited Scalar Strings

- Good, because existing scalar serialization could be reused.
- Bad, because delimiter escaping becomes a second input grammar and violates
  the array value contract.

## More Information

- [ADR-0017](0017-bind-pack-parameters-before-rendered-ownership.md)
- [ADR-0044](0044-render-lifecycle-script-arguments.md)
- [Pack manifest reference](../../../developer/packs/reference/manifest.md)
- [Parameter and variable guide](../../../developer/parameters-and-variables.md)
