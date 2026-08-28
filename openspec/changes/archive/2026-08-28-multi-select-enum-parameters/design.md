## Context

See [proposal.md](proposal.md) for motivation. Parameter declarations and
binding sources currently carry scalar string or boolean values. The dedicated
managed-file condition parser supports scalar comparisons, while Scriban
receives resolved values for managed files, lifecycle instructions, and script
arguments. This change must preserve those trust, preflight, and transaction
boundaries while adding one collection-shaped value.

## Goals / Non-Goals

**Goals:**

- Use one ordered, unique string-array representation after binding, regardless
  of whether selections came from CLI input, a default, a project variable, a
  composite binding, or an interactive prompt.
- Keep scalar enum behavior and existing version-1 manifests compatible.
- Validate declarations and binding values before lifecycle planning mutates
  files or state.
- Keep managed-file condition syntax constrained and independent from Scriban.

**Non-Goals:**

- General list parameters, multi-select strings without a declared value set,
  nested collection values, or collection operators beyond membership.
- Set algebra, dynamic operands, or Scriban expressions inside managed-file
  conditions.
- Persisting resolved parameter values in `lunapack-lock.yml`.

## Decisions

### Represent multi-select values as ordered unique string arrays

`multiple` is valid only for enum declarations and defaults to false. Once a
multi-select enum resolves, every consumer receives an ordered string array.
Validation rejects duplicates instead of silently deduplicating, preserving
clear diagnostics and deterministic input. Optional unresolved parameters bind
an empty array. A required parameter still needs a value source, but an
explicit array source may be empty because empty selection is a valid value.

Alternative: model the value as an unordered set. Rejected because YAML,
Scriban, prompts, and diagnostics expose sequence order, and no persisted set
type exists in the contracts.

### Repeat CLI assignments to collect selections

Each `-p features=value` occurrence appends one selection. Repeated names remain
errors for scalar parameters; repeated values are errors for multi-select
parameters. This extends the existing repeatable option without introducing a
comma-escaping grammar. Prompts must provide a multi-choice control when the
terminal abstraction supports one, with deterministic fallback input otherwise.

Alternative: accept comma-separated values in one assignment. Rejected because
enum values may eventually contain punctuation and shell quoting would become
part of the data contract.

### Permit string arrays only at generic binding boundaries

The project variable and composite binding schemas cannot inspect the target
parameter declaration, so they allow unique string arrays structurally.
Graph-aware runtime binding verifies that arrays target multi-select enums and
that every element belongs to the controlling declaration. Scalar values remain
unchanged. The root-nearest declaration continues to control enum values, but
all same-name declarations must agree on scalar versus multi-select shape.

Alternative: encode arrays as strings at generic boundaries. Rejected because
it violates the array value contract and creates multiple parsing rules.

### Extend the condition parser with typed literal membership

Add one binary form, `"literal" in identifier`, at the comparison precedence
level. The right operand must resolve to a declared multi-select enum; the left
operand must be a string literal. Existing `!`, `&&`, `||`, parentheses, and
scalar comparisons retain their current semantics. Parse and type errors remain
preflight failures.

Alternative: run managed-file conditions through Scriban. Rejected because that
would widen the condition language and collapse the existing trust boundary.

### Adapt arrays once at the Scriban boundary

The shared strict renderer converts resolved multi-select arrays into Scriban
arrays for managed files, instruction templates, and script argument templates.
Implementation first verifies whether the pinned Scriban version supports the
documented `features contains "docker"` expression for arrays. If it does, use
that behavior directly. If it does not, expose only an equivalent array
membership operator or function without adding host access or relaxing strict
variable handling.

Alternative: expose one boolean per selection. Rejected because templates must
receive the declared parameter as an array and independent booleans can drift
from the source value.

### Treat schema and lifecycle semantics as a durable contract extension

Update the pack and project schemas without changing schema version because all
new declaration fields and value forms are optional and old documents retain
their meaning. Record the representation and validation boundary in a new ADR;
do not alter prior accepted ADRs. Public docs describe author and consumer use,
internal docs describe binding and parser invariants, and product docs record
the observable capability.

Alternative: increment schema versions. Rejected because readers already reject
unknown properties while old valid documents need no migration or reinterpretation.

## Risks / Trade-offs

- [Generic variables can contain arrays that no pack uses] -> Keep schema
  structural and issue declaration-specific errors only during graph binding.
- [CLI input order differs across binding sources] -> Preserve source order and
  test CLI, YAML default, variable, composite, and prompt paths.
- [Scriban membership syntax differs by library version] -> Add a focused
  executable characterization test before choosing built-in or adapter behavior.
- [Required empty arrays surprise users] -> Document that `required` requires a
  source, not a non-empty selection, and show optional empty-array behavior.
- [Condition grammar regresses scalar expressions] -> Retain existing parser
  precedence and add mixed membership/conjunction regression tests.

## Migration Plan

1. Extend schema and domain contracts, then verify all existing manifests and
   scalar enum fixtures remain valid.
2. Add graph-aware array binding and CLI/prompt collection with failure-before-
   mutation tests across every binding source.
3. Extend condition parsing and the shared Scriban conversion boundary, then
   exercise managed files and all templated lifecycle surfaces end to end.
4. Update authoring commands, public/internal/product documentation, ADR index,
   and changelog; validate schemas, Markdown, CLI tests, and Native AOT publish.

Rollback removes the optional field and array support before release. Existing
scalar manifests require no migration, and resolved selections are not stored
in lock state. After release, packs using `multiple` require a supporting CLI,
so rollback requires those packs to return to scalar or boolean parameters.
