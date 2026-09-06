## Context

See [proposal.md](proposal.md) for motivation. Composite references currently carry parameter bindings, conditions, and disabled lifecycle events, while managed-target remapping is assembled only from command input and `lunapack.yml`. `ResolvedPackGraph` retains active references but collapses traversal ancestry, so installation planning cannot determine which reference-level defaults apply to a descendant or resolve competing paths through a diamond graph.

Lifecycle conditions currently use the managed-file parameter expression evaluator and are resolved while hooks are planned. False declarations disappear before script authorization, and authorization removes scripts that policy, mode, or individual consent prevents from running. Runtime-dependent conditions therefore require preserving declaration identity and state through authorization and dispatch without weakening the rule that every potentially executable script is authorized before any hook is processed.

## Goals / Non-Goals

**Goals:**

- Carry deterministic composite-reference remap context from graph selection into managed-file target resolution.
- Reuse existing remap validation, path normalization, exact-file precedence, `@ignore`, ownership, and lock semantics.
- Extend lifecycle conditions without exposing runtime-only functions to managed-file selection, composite selection, or parameter requiredness.
- Reuse the condition expression engine for typed transient parameter bindings
 without changing existing literal binding semantics.
- Preserve up-front authorization and existing rollback boundaries.
- Keep existing manifests and hooks unchanged when they use neither feature.

**Non-Goals:**

- Persist pack-authored composite remaps into `lunapack.yml` or the lock file as consumer configuration.
- Relocate already managed files during update; recorded lock targets remain authoritative.
- Carry previous-script state across packs or lifecycle events.
- Continue hook processing after script execution failure or cancellation.
- Add a general-purpose workflow language or arbitrary access to lifecycle internals from expressions.
- Support string interpolation, arithmetic, object construction, or an
 unbounded template language in parameter bindings.

## Decisions

### Preserve graph path context for composite remaps

Graph selection will derive an effective composite remap context for each selected pack. Traversal starts with no pack-authored mapping at each requested root, inherits mappings down each active reference, and overlays the current reference before visiting its target. The most recently traversed reference is therefore the nearest and wins within a path. If multiple active paths reach the same pack, equivalent effective contexts collapse; conflicting mappings at the same winning depth produce a preflight error instead of depending on declaration or traversal order.

Managed-file planning will apply the resulting composite context as the lowest-precedence remap source. Existing consumer layers remain ordered as command line, requested-root configuration, top-level project configuration, then composite context. Existing exact-file-over-directory selection applies independently within each source. Remap diagnostics gain composite parent/reference provenance so dry runs explain the effective target.

Alternatives considered:

- Limit remaps to the directly referenced pack. Rejected because the requested contract intentionally adapts the complete consumed subtree.
- Let ancestor remaps win. Rejected because the reference closest to a dependency has the most specific knowledge of its layout.
- Choose one diamond path by traversal order. Rejected because manifest reordering would silently change filesystem output.

### Reuse the remapping shape on pack references

`PackManifest.PackReference` will gain the same `remap.directories` and `remap.files` shape used by requested project packs. Schema and model validation will apply `ProjectPath`-based normalization and the existing `@ignore` exception. Authoring commands will expose repeatable directory and file mapping options when adding or replacing a composite reference and include mappings in reference listing.

The shared data shape avoids a second remap syntax, but pack-authored remaps remain separate provenance rather than being merged into consumer configuration. This preserves the distinction between a pack's layout default and a repository owner's override.

Alternative considered: introduce a composite-only target-prefix property. Rejected because it cannot express exact file renames, exceptions beneath directory mappings, or `@ignore` consistently.

### Add context-aware condition validation

The condition parser will retain one expression grammar and typed AST, but validation will receive an allowed-function context. Parameter expressions continue to allow existing operators and `isDefault`. Lifecycle-hook expressions additionally allow zero-argument `scriptsSkipped()` and `previousScriptState()`. Other declaration types reject these functions before mutation. `previousScriptState()` comparisons validate against the closed state set rather than accepting arbitrary strings.

Alternative considered: add a separate lifecycle expression parser. Rejected because it would duplicate tokenization, operators, parameter lookup, diagnostics, and future maintenance.

### Use exact expression markers for typed composite bindings

A composite parameter string is an expression only when its complete trimmed
value matches `${{ expression }}`. All other strings remain literals. This
preserves every existing manifest while making expression intent explicit.

The shared expression parser will expose a typed value mode in addition to its
existing Boolean condition mode. A direct parameter identifier returns its
resolved type and value. `iif(condition, whenTrue, whenFalse)` evaluates the
existing Boolean grammar for its first argument and returns one of two typed
value expressions. Value branches support quoted strings, Boolean parameters,
scalar parameters, multi-select parameters, and nested `iif` calls. Both
branches must have compatible types before evaluation; the selected result must
also match the referenced parameter declaration. Lifecycle-only functions are
never valid in binding expressions.

Expressions are scoped to parameters declared by the pack that owns the
reference. This prevents a composite from reaching through implementation
details of unrelated graph branches. Resolution first binds ordinary consumer,
project, and default inputs needed by the referencing pack, then evaluates
active composite expressions in dependency order. A dependency graph detects
self-reference and cycles and makes evaluation independent of manifest order.
The computed result remains a fixed composite binding under existing precedence
and suppresses prompting for the referenced transient parameter.

Alternatives considered:

- Treat every binding string as an expression. Rejected because existing enum
 and string literals would become ambiguous or invalid.
- Add Scriban interpolation. Rejected because bindings need typed values and
 preflight dependency analysis, not rendered strings.
- Add a C-style ternary operator. Rejected in favor of `iif`, which fits the
 existing function grammar and avoids introducing `?` and `:` precedence.

### Preserve declarations and evaluate runtime state during dispatch

Lifecycle planning will preserve every non-disabled hook declaration plus its pack, event, and position. Conditions containing no runtime-state function remain eligible for early evaluation so false static script conditions do not trigger authorization. Runtime-dependent script declarations remain authorization candidates because their final inclusion cannot be known before earlier scripts execute. All candidates are resolved and authorized before the first hook is processed.

Dispatch will maintain a previous-script state keyed by pack and lifecycle event. The key starts at `none`; a false script condition records `ignored`; invocation-wide suppression or individual authorization decline records `skipped`; successful execution records `succeeded`. Execution failure records `failed`, and execution cancellation records `cancelled`, immediately followed by existing abort and rollback behavior. Instruction declarations never replace previous-script state.

`scriptsSkipped()` comes from invocation policy established before dispatch. It is true only for effective whole-invocation script skip or trust-policy denial. It does not infer a global state from absent scripts, `disabledHooks`, false conditions, or individual consent decisions.

Alternative considered: authorize runtime-dependent scripts only when reached. Rejected because instructions or earlier filesystem mutation could occur before executable trust is resolved.

### Treat runtime-dependent dry-run outcomes as unknown

Dry run will parse and validate every potentially reachable runtime-dependent hook, including instruction files and script command resolution where current dry-run policy requires it. Output will mark the hook runtime-conditional and show its expression instead of evaluating `previousScriptState()` against fictional execution. Conditions depending only on parameters or `scriptsSkipped()` may still resolve because those inputs are known before execution.

Alternative considered: assign every dry-run script the state `skipped`. Rejected because dry-run itself is not one of the user-selected or policy-enforced skip modes and would produce misleading fallback plans.

## Risks / Trade-offs

- [Subtree remaps can affect many packs] → Include parent reference provenance in dry-run output and reject ambiguous diamond mappings before mutation.
- [Runtime-dependent scripts may request authorization but later be ignored] → Explain the condition in authorization and dry-run output; preserve up-front authorization as the stronger security invariant.
- [Keeping ignored declarations increases lifecycle planner complexity] → Use one explicit hook-state record rather than parallel collections or inferred list positions.
- [New functions make lifecycle conditions context-sensitive] → Require an explicit expression-use context and retain focused parser and validator tests for every forbidden declaration type.
- [Pack-authored remaps could surprise existing consumers] → Existing manifests are unchanged, consumer mappings always win, and lock-recorded targets prevent update-time relocation.

## Migration Plan

1. Add the optional composite-reference remap schema and model while preserving compatibility for omitted mappings.
2. Add graph-path remap resolution and diagnostics behind the new property, then cover direct, nested, diamond, consumer-override, ignore, update, and dry-run cases.
3. Extend condition parsing and validation with declaration context and closed previous-state values.
4. Refactor lifecycle planning, authorization, and dispatch to preserve declarations and state, then add runtime and dry-run coverage.
5. Extend authoring commands and update public and internal documentation, including a new ADR for precedence and runtime evaluation.
6. Extend the shared expression engine and composite parameter resolver with
 typed `${{ ... }}` and `iif` bindings, then document the durable grammar in a
 new ADR and public examples.

Rollback consists of reverting the feature before packs depend on the new schema. Once published packs use composite remaps or runtime-state functions, older LunaPack versions will reject those manifests through existing unknown-property/function validation; no automatic downgrade transformation is planned.
