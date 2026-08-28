## Context

See [proposal.md](proposal.md) for motivation and
[specs/pack-template-rendering/spec.md](specs/pack-template-rendering/spec.md)
for behavior. `PackInstallationPlanner` currently evaluates a managed-file
condition, resolves that selector's effective target, and renders its content in
one pass. `PackTemplateRenderer` receives only source path and resolved
parameters, so it cannot resolve references against later files or identify the
current effective target. Install, update, and dry-run already consume the same
installation planner. `ProjectPath` owns portable project-path normalization,
and `CliConsole` owns warning presentation.

Directory and glob selectors expand one manifest declaration into concrete
managed files. Resolution therefore needs the expanded declared and effective
target pairs, not only the selector-level target. Lifecycle instruction
templates and script-argument templates use the same renderer but have no
managed-file effective target.

## Goals / Non-Goals

**Goals:**

- Make the complete condition-selected target set available before any managed
  file content is rendered.
- Keep path lookup lexical, deterministic, platform-independent, and scoped to
  immutable resolved-plan data.
- Carry recoverable rendering warnings through planning without coupling the
  renderer to terminal output.

**Non-Goals:**

- Expose project files, environment state, path existence, globbing, or source
  content through Scriban.
- Add managed-file path functions to lifecycle instructions or script
  arguments, which lack a current managed-file target.
- Resolve arbitrary project paths or references outside selected managed files.
- Change manifest, project configuration, or lock-file schemas.

## Decisions

### Expand and resolve targets before rendering content

Split managed-file planning into two phases. The first phase evaluates
conditions, expands file, directory, and glob selectors, resolves every
concrete declared target to its effective project-relative target, and performs
target identity validation. The second phase renders and completes the existing
target preflight for each prepared candidate.

Build one immutable lookup from the first phase. Include only selected concrete
files. Index by normalized declared target and retain a value only when that key
identifies exactly one file; duplicate keys remain unresolved. This makes
references independent of manifest order and naturally gives conditionally
excluded files the required fallback.

Alternative considered: add candidates to the lookup while rendering. That
makes forward references fail and renders output dependent on graph order.

### Pass an explicit managed-file rendering context

Add a managed-file-specific rendering context containing the current file's
effective target and the immutable target lookup. `PackTemplateRenderer` will
add a read-only `files` Scriban object only when this context is present. Its
two callable members implement `path` and `relative_path`; parameter-only
callers continue using the existing context and do not gain these functions.

The object exposes only delegates over strings and plan data. It receives no
`IFileSystem`, project root, source path, or service object, preserving strict
Scriban variables and the no-filesystem boundary.

Alternative considered: make the resolved graph or planner available to
Scriban. That leaks unrelated lifecycle state and creates a much larger template
API contract.

### Calculate relative paths lexically and normalize once

For `files.path`, return the indexed effective project-relative target. For
`files.relative_path`, take the directory portion of the current effective
target and calculate a lexical relative path to the referenced effective target
through the existing path abstraction. Normalize the result through
`ProjectPath.Normalize` before returning it. No existence check or file read is
part of either operation.

Alternative considered: URI-based relative resolution. URI escaping and
directory semantics do not match project-relative filesystem paths and would
add conversion rules without improving confinement.

### Treat fallback warnings as successful plan diagnostics

Return rendered bytes together with recoverable template diagnostics. Aggregate
those diagnostics into `PackInstallationPlan`, then let lifecycle orchestration
emit each warning through `CliConsole` after planning succeeds. Use the current
effective target in the message, for example:

```text
Managed file target 'docs/development/code-review.md' could not be resolved while rendering '.github/agents/core-review.agent.md'.
```

The resolver returns the supplied target unchanged after recording the warning.
Install, update, and dry-run all receive diagnostics from the same planner path.
Pack-author validation may inspect or discard diagnostics but must not turn them
into validation failures.

Alternative considered: inject `CliConsole` into the renderer. That mixes
presentation with deterministic planning, complicates tests, and can emit
warnings from a plan that later fails for another reason.

### Record and document the template API boundary

Add ADR-0057 for the resolved-plan-only Scriban API and update internal
path-handling guidance. Add pack-author reference examples and fallback behavior
to developer documentation, align product requirements, and add a changelog
entry because templates gain externally observable behavior.

## Risks / Trade-offs

- Two-phase planning can accidentally change existing preflight order -> retain
  current selector expansion order and add regression tests for failures and
  rendered output.
- Duplicate declared targets make lookup ambiguous -> exclude ambiguous keys
  from the lookup and exercise the standard warning fallback.
- Platform path APIs can introduce `\` separators -> normalize every function
  result with `ProjectPath` and test Windows-style path behavior explicitly.
- Repeated unresolved calls can produce noisy output -> preserve one diagnostic
  per call initially; warning deduplication can be added later without changing
  resolution results.
- Shared renderer callers could accidentally gain `files` -> require the
  managed-file context explicitly and test strict-variable failure in other
  template contexts.

## Migration Plan

1. Introduce target-candidate and rendering-context models plus focused resolver
   tests.
2. Refactor installation planning into target expansion followed by rendering,
   retaining existing conflict and strategy behavior.
3. Add the read-only Scriban `files` object, relative calculation, fallback
   diagnostics, and lifecycle warning presentation.
4. Add install, update, and dry-run parity tests, then update product, internal,
   developer, ADR, and changelog documentation.

No persisted data migration is required. Rollback removes the template object
and returns planning to single-pass rendering; existing manifests, project
configuration, and lock files remain compatible.
