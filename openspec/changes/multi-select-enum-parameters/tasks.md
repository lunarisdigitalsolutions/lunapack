## 1. Contracts And Schema

- [x] 1.1 Add `multiple` and scalar-or-array default support to parameter
      declaration models and YAML serialization while preserving omitted-field
      scalar enum behavior.
- [x] 1.2 Extend `pack.schema.json` for enum-only `multiple`, unique allowed
      array defaults, and string-array composite bindings; extend
      `lunapack.schema.json` for unique string-array variables.
- [x] 1.3 Add schema and manifest-validation fixtures covering valid empty and
      populated arrays, wrong value shapes, duplicates, unknown selections,
      non-enum `multiple`, and existing scalar manifests.

## 2. Parameter Resolution And Input

- [x] 2.1 Extend resolved parameter values and graph declaration compatibility
      to carry ordered arrays and reject scalar-versus-multi-select conflicts.
- [x] 2.2 Bind and validate defaults, project variables, and composite reference
      arrays against the controlling enum declaration, including optional empty
      resolution and required value-source semantics.
- [x] 2.3 Collect repeated `--parameter` and `-p` assignments for multi-select
      enums while retaining duplicate-name failures for scalar parameters and
      rejecting duplicate or unknown selections.
- [x] 2.4 Add interactive multi-select prompting with deterministic ordering and
      default handling through the existing terminal abstraction.
- [x] 2.5 Add focused binder, graph, CLI parsing, prompt, precedence, and
      failure-before-mutation tests for every multi-select value source.

## 3. Conditions And Scriban

- [x] 3.1 Extend the managed-file condition tokenizer, parser, type checker, and
      evaluator with `"literal" in identifier` at comparison precedence for
      multi-select enums only.
- [x] 3.2 Add condition tests for present, absent, and empty selections; combined
      `&&`, `||`, and parentheses; malformed expressions; undeclared parameters;
      and incompatible operand types.
- [x] 3.3 Characterize array membership syntax in the pinned Scriban version,
      then expose resolved arrays through the shared strict renderer using built-in
      behavior or the smallest constrained adapter needed for `contains`.
- [x] 3.4 Add renderer and lifecycle tests proving identical membership behavior
      for managed files, lifecycle instructions, and script arguments, including
      strict failures and unchanged transaction behavior.

## 4. Pack Authoring

- [x] 4.1 Extend pack parameter set/list operations to accept, display, and
      round-trip multi-select declarations and ordered array defaults.
- [x] 4.2 Add authoring command tests for valid multi-select enums and no-change
      failures for incompatible `multiple`, duplicate defaults, and unknown values.

## 5. Documentation And Validation

- [x] 5.1 Update relevant `docs/product` requirements for consumer-visible
      multi-select parameter, condition, and template behavior.
- [x] 5.2 Update `docs/developer` manifest, parameter/variable, installation,
      condition, template, and pack-authoring guidance with array and CLI examples.
- [x] 5.3 Add maintainer guidance in `docs/internal`, create the next ADR from
      the template for array representation and validation boundaries, add it to
      the ADR index, and add the external feature to `CHANGELOG.md`.
- [x] 5.4 Run schema validation, focused CLI tests, the complete CLI test suite,
      Markdown/link checks, locked restore, and Release Native AOT publish.
