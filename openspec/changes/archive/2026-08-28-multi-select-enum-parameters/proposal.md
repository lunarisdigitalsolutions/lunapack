## Why

Enum parameters currently resolve to one scalar value, forcing pack authors to
model independent feature choices as separate booleans. Multi-select enums let
one declared option set drive file selection and template rendering consistently.

## What Changes

- Allow enum parameter declarations to set `multiple: true` and use array
  defaults containing zero or more unique allowed values.
- Resolve multi-select enum inputs as arrays and reject every selected value not
  declared by the parameter.
- Add the `in` membership expression for multi-select enum managed-file
  conditions, including conjunction with existing condition operators.
- Expose resolved multi-select enum values to Scriban as arrays and use
  Scriban's existing array membership behavior when suitable.
- Extend pack-authoring operations to preserve and validate the multi-select
  enum declaration shape.
- Preserve existing scalar enum declaration, input, condition, and template
  behavior when `multiple` is omitted or false.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `manifest-schemas`: Define the enum-only `multiple` property and array default
  validation in `pack.yml`.
- `local-pack-lifecycle`: Resolve, validate, and propagate multi-select enum
  values through explicit input, variables, defaults, prompts, and composite
  pack bindings.
- `pack-template-rendering`: Evaluate multi-select membership in managed-file
  conditions and expose selected values as Scriban arrays.
- `pack-authoring`: Let authoring commands create, inspect, and preserve
  multi-select enum declarations.

## Impact

Affected surfaces include the pack JSON Schema; manifest, parameter-binding,
condition-expression, and template-rendering models; install and pack-authoring
CLI input; composite parameter compatibility; and focused schema, parser,
lifecycle, and Scriban tests. Public guidance in `docs/developer` must document
the manifest shape, input representation, conditions, and templates. Maintainer
guidance in `docs/internal` must record binding and expression semantics, while
relevant `docs/product` requirements must reflect the consumer-visible feature.
Because this extends durable manifest and lifecycle contracts, implementation
requires an ADR and changelog entry.
