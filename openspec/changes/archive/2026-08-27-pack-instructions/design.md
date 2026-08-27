## Context

See [proposal.md](proposal.md) for motivation. Today `PackManifest` models one optional script per lifecycle event, `LifecycleHookPlanner` produces separate pre- and post-mutation script invocation lists, and `PackLifecycleService` authorizes all scripts before executing either list inside the existing update transaction. `CliConsole` already exposes terminal interactivity, and `PackTemplateRenderer` provides strict Scriban rendering over resolved graph parameters.

The change crosses the public manifest schema, source materialization, lifecycle planning and execution, pack authoring, catalog inspection, and documentation. One bundled pack and lifecycle test fixtures currently use the removed `scripts` shape. No Markdown parser dependency exists, and the requested step grammar is intentionally limited to H2 and H3 headings.

## Goals / Non-Goals

**Goals:**

- Preserve one deterministic ordered hook plan across script and instruction types.
- Complete path confinement, content loading, Scriban rendering, step parsing, and script authorization before lifecycle side effects begin.
- Keep script trust, process isolation, integrity checks, rollback, and `--scripts` behavior unchanged.
- Keep instruction display independent from script trust and non-blocking when the console is not interactive.

**Non-Goals:**

- General Markdown rendering or a CommonMark-compatible parser.
- Automatic migration of third-party manifests using `scripts`.
- Persisted instruction progress, completion claims, or instruction-specific trust.

## Decisions

### Use ordered typed hook lists

Replace `scripts` with event-keyed arrays under `hooks`:

```yaml
hooks:
  preInstall:
    - type: instruction
      file: instructions/pre-install.md
      templating: true
    - type: script
      file: scripts/setup.ps1
      runner: pwsh
      arguments:
        - -File
```

Each event list preserves author order and accepts multiple declarations. A single manifest model with a required `type` plus nullable type-specific properties will mirror the JSON Schema; `ManifestModelValidator` will enforce the same union constraints after YAML deserialization. This avoids polymorphic YAML configuration while keeping validation errors explicit.

Alternatives considered: separate top-level `scripts` and `instructions` cannot define mixed ordering; one hook per event prevents multi-step automation and guidance; nested kind properties impose fixed ordering.

### Make the schema migration intentionally breaking

The schema and model will stop accepting top-level `scripts`. Existing declarations migrate mechanically into `hooks.<event>` list items with `type: script`; script fields retain their current names and meanings. The bundled `commitlint` pack and all fixtures will migrate in the same change. Validation and documentation will show the equivalent new shape rather than maintaining dual representations.

Alternative considered: accepting both shapes would reduce immediate breakage but creates ordering and precedence rules that would become another long-lived public contract.

### Plan typed hooks, authorize scripts, then dispatch in order

Evolve lifecycle planning to produce immutable prepared hook entries carrying pack identity, event, event position, and one typed payload:

- Script payloads retain rendered arguments and confined packed-file evidence.
- Instruction payloads retain the prepared introduction and step sequence.

`--skip-instructions` removes instruction entries before their files are loaded. Remaining instruction files are confined beneath the resolved pack snapshot, decoded as strict UTF-8, optionally rendered through `PackTemplateRenderer`, and parsed before authorization. The authorizer receives every script payload from both phases before any prepared hook is processed. Authorized script commands are then associated back to their prepared entries; `--scripts skip` removes script entries without affecting instructions.

Pre-mutation hooks run before the existing update transaction applies managed-file changes. Post-mutation hooks run after managed-file mutation and before state persistence, preserving rollback when a later script or interactive operation fails. Dependency order remains owned by `PackLifecyclePlan`; declaration order applies within each pack event.

Alternatives considered: separate script and instruction pipelines lose manifest ordering; lazy instruction loading can expose template or path failures after scripts execute or managed files change.

### Use a limited line-oriented step parser

Add a focused parser for lines beginning with `##` or `###` followed by a space. It will:

- collect content before the first step as one introduction;
- create major steps from H2 headings;
- create child steps from H3 headings after an H2;
- treat H3 headings before any H2 as top-level steps;
- preserve all non-step lines as display content;
- create one untitled step containing the complete document when no step heading exists.

The presenter strips detected step markers, prints generated numbering and titles, and otherwise writes content as text. It does not interpret links, fenced code blocks, checkboxes, or other Markdown constructs. This keeps behavior aligned with the bounded grammar and avoids a new dependency.

Alternative considered: a full Markdown syntax tree would avoid treating heading-like lines inside code fences as steps, but expands dependency and rendering scope explicitly excluded from this change.

### Separate preparation from interactive presentation

Add an instruction presenter behind the typed hook dispatcher. Interactive consoles display the introduction once, then one step at a time and wait for Enter between steps, including the final step. Non-interactive consoles display the complete prepared sequence without reading input. Prompts say only `Press Enter to continue...`; they do not claim completion.

Dry-run uses the same path, template, and parser preparation but sends only pack ID, event, file, templating state, and step count to the generalized hook formatter. It never enters guided presentation.

Alternative considered: requiring `--skip-instructions` in automation would fail or hang existing install/update workflows; suppressing output would hide required manual setup.

### Make authoring and inspection position-aware

Replace script-specific authoring commands with `pack add hook`, `pack hooks`, and `pack rm hook`. Add appends by default, `--replace <position>` targets a one-based position within one event list, and removal uses the same event plus position. This supplies stable user-facing selection without adding manifest IDs solely for editing.

Resolved-pack inspection will list lifecycle events in canonical order and hooks in declaration order. Script rows retain command, argument, and description details; instruction rows show file and effective templating state. Composite `disabledHooks` remains event-based and suppresses every typed hook in that event.

Alternative considered: generated hook IDs add schema surface and authoring overhead without runtime value.

### Document the new lifecycle boundary

Implementation will add an ADR for the accepted unified hook model and update product requirements, maintainer lifecycle guidance, CLI command reference, install/update guidance, script trust guidance, and pack authoring examples. The changelog will identify the manifest and command migration as breaking.

## Risks / Trade-offs

- **Legacy packs using `scripts` stop validating** -> Publish exact before/after YAML and command migrations; migrate bundled content and fixtures atomically.
- **A heading-like line inside a code block becomes a step** -> Document the intentionally limited parser and keep special code-block handling out of scope.
- **Many hooks or long documents produce noisy non-interactive output** -> Preserve declared order and complete output; consumers can use `--skip-instructions` explicitly.
- **Interactive cancellation during post-mutation guidance requires rollback** -> Keep post hooks inside the existing transaction before state persistence.
- **Position-based authoring selectors shift after removal** -> Always list current one-based positions and require event plus position for mutation.

## Migration Plan

1. Add schema and model support for typed `hooks`, remove `scripts`, and migrate the bundled `commitlint` manifest and test fixtures.
2. Generalize lifecycle planning, dry-run formatting, authorization integration, and ordered dispatch while retaining script-specific security components.
3. Add instruction preparation, parsing, and presentation plus install/update `--skip-instructions` handling.
4. Replace script-specific authoring commands and inspection output with hook-aware forms.
5. Update product, internal, developer, ADR, and changelog documentation with manifest and CLI migration examples.
6. Validate schemas, focused unit tests, lifecycle integration tests, the complete CLI test suite, and Native AOT publication.

Rollback requires reverting CLI/schema changes and restoring the bundled manifest's former `scripts` shape together; no persisted project or lock-file migration is introduced.
