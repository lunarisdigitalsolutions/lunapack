## 1. Manifest Contracts

- [x] 1.1 Add `remap.directories` and `remap.files` to composite pack references in the manifest model and `pack.schema.json`; verify schema tests accept valid paths and `@ignore` while rejecting empty, rooted, escaping, duplicate, and unknown values.
- [x] 1.2 Extend manifest model validation to normalize composite remap paths through `ProjectPath` and validate lifecycle-only runtime functions and closed previous-state values; verify focused validator tests cover allowed and forbidden expression contexts.

## 2. Composite Remap Resolution

- [x] 2.1 Preserve active graph path and reference provenance needed to derive inherited remap contexts; verify graph tests cover direct, nested, inactive, equivalent diamond, and conflicting same-depth references.
- [x] 2.2 Apply nearest-reference composite remaps as the lowest-precedence managed-target source while retaining exact-file, directory-suffix, `@ignore`, consumer override, and lock-target behavior; verify focused installation and update planner tests.
- [x] 2.3 Report composite parent/reference provenance in remap diagnostics and dry-run output; verify formatter and CLI integration tests show the selected source and effective target.

## 3. Runtime Hook Conditions

- [x] 3.1 Extend the shared condition AST and evaluator with declaration-aware validation for `scriptsSkipped()` and `previousScriptState()`; verify parser and evaluator tests cover composition, return types, every supported state, invalid arguments, and forbidden declaration types.
- [x] 3.2 Preserve lifecycle declarations through planning and preauthorize every potentially executable runtime-dependent script before processing hooks; verify planner and authorizer tests retain static-condition omission and existing trust guarantees.
- [x] 3.3 Track previous script state per pack and lifecycle event during ordered dispatch, including `none`, `ignored`, global and individual `skipped`, `succeeded`, `failed`, and `cancelled`; verify lifecycle tests cover instruction fallbacks, event isolation, authorization decline, and abort/rollback behavior.
- [x] 3.4 Evaluate `scriptsSkipped()` from effective invocation mode and trust-policy denial without inferring it from absent, disabled, ignored, or individually declined scripts; verify install, update, and uninstall tests cover each source of suppression.
- [x] 3.5 Validate and label runtime-dependent hooks during dry run without assuming execution outcomes; verify dry-run tests cover known `scriptsSkipped()` results and unknown `previousScriptState()` results.

## 4. Authoring Experience

- [x] 4.1 Add repeatable file and directory remap options to composite-reference add and replace commands, preserve mappings atomically, and list them with reference context; verify authoring command tests cover add, replace, list, remove, and invalid paths.
- [x] 4.2 Preserve and display runtime-state hook conditions through hook add, replace, and list commands; verify authoring tests cover valid fallback expressions and unsupported states.

## 5. Documentation And Validation

- [x] 5.1 Create the next ADR from the repository template and index it, documenting subtree inheritance, remap precedence, diamond conflicts, runtime condition timing, and up-front script authorization; verify Markdown links and ADR numbering.
- [x] 5.2 Update internal path/lifecycle documentation and developer manifest, remapping, and hook-condition guidance with examples; verify documentation distinguishes pack-authored defaults from consumer overrides and documents every state.
- [x] 5.3 Add the externally observable CLI and manifest changes to the CLI changelog and verify both npm and NuGet package inclusion remains intact.
- [x] 5.4 Run focused unit and integration suites for schema, graph, planning, lifecycle, dry-run, and authoring behavior, then run repository formatting, build, and full test validation with no new failures.

## 6. Transient Parameter Expressions

- [x] 6.1 Extend composite binding schema/model validation for exact `${{ ... }}` values while preserving literal strings and arrays; verify pass-through, `iif`, malformed marker, forbidden runtime function, and unknown parameter cases.
- [x] 6.2 Extend the shared condition parser with typed value expressions and `iif(condition, whenTrue, whenFalse)` without duplicating tokenization or Boolean semantics; verify scalar, Boolean, enum, multi-select, nested, and incompatible-branch behavior.
- [x] 6.3 Resolve active expression bindings against parameters declared by the referencing pack, detect unresolved dependencies and cycles, preserve existing composite precedence, and validate the result against the referenced declaration; verify resolver and prompt tests.
- [x] 6.4 Preserve expression bindings through reference add, replace, list, and manifest serialization; verify authoring mutations remain atomic for invalid expressions.
- [x] 6.5 Add a new ADR and update public manifest, composition, parameter, and authoring guidance plus the CLI changelog with exact syntax, evaluation scope, precedence, and examples.
- [x] 6.6 Run focused expression/schema/resolver/authoring tests, repository formatting, strict OpenSpec validation, Release build, and relevant full test suites with no new feature failures.
