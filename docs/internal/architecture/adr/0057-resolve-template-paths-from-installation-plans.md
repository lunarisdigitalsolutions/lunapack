---
status: accepted
date: 2026-08-28
---

# ADR-0057: Resolve Template Paths From Installation Plans

## Context and Problem Statement

Pack authors can reference one managed file from another, but consumers can
remap declared targets to repository-specific effective targets. Hardcoded
references therefore become invalid after remapping. Templates need portable
path resolution without gaining access to project files or other host services.

## Decision Drivers

- Resolve references consistently during install, update, and dry-run planning.
- Use declared targets as stable pack-authored identities and effective targets
  as rendered locations.
- Preserve the strict Scriban boundary and platform-independent path output.
- Keep unavailable conditional or missing references recoverable.

## Considered Options

- Resolve paths from the complete condition-selected installation plan.
- Resolve paths incrementally while each managed file is planned.
- Expose project filesystem path operations to templates.

## Decision Outcome

Chosen option: "Resolve paths from the complete condition-selected installation
plan", because it supports forward references, remapping, directory and glob
expansion, and lifecycle parity without exposing filesystem access.

Managed-file planning expands and resolves every selected concrete target before
rendering any template. Managed-file Scriban contexts receive a read-only
`files` object backed only by that immutable target map. `files.path` returns an
effective project-relative target. `files.relative_path` calculates a lexical
relative path from the current template's effective target directory. Both
return slash-only paths.

Missing, conditionally excluded, or ambiguous declared targets emit a warning
and return the supplied target unchanged. Lifecycle instruction and script
argument templates do not receive `files` because they have no current managed
target.

### Consequences

- Good, because references are independent of manifest order and consumer
  remapping.
- Good, because install, update, and dry-run share one resolution path.
- Good, because templates cannot discover, read, or test project files.
- Bad, because planning needs a candidate-expansion phase before content
  rendering.
- Bad, because repeated unresolved calls can produce repeated warnings.

### Confirmation

Renderer tests cover effective and relative paths, slash normalization,
fallback diagnostics, and context isolation. Planner tests cover forward,
directory, glob, conditional, and ambiguous references. Lifecycle tests compare
install, update, and dry-run results and warning behavior. Native AOT publishing
confirms the Scriban function binding remains trimming-compatible.

## Pros and Cons of the Options

### Resolve from the complete installation plan

- Good, because every selected concrete target is available before rendering.
- Good, because lookup uses no mutable or external state.
- Bad, because the planner becomes explicitly two-phase.

### Resolve incrementally

- Good, because it requires less planner restructuring.
- Bad, because forward references fail and output depends on graph order.

### Expose filesystem path operations

- Good, because templates could resolve arbitrary project locations.
- Bad, because results would depend on current filesystem state.
- Bad, because it expands the template trust boundary beyond managed content.

## More Information

- [ADR-0027](0027-require-explicit-template-rendering.md)
- [ADR-0036](0036-record-declared-and-effective-managed-targets.md)
- [ADR-0037](0037-canonicalize-persisted-project-paths.md)
- [Pack template rendering specification](../../../../openspec/specs/pack-template-rendering/spec.md)
- [Use Scriban templates](../../../developer/packs/how-to/use-scriban-templates.md)
