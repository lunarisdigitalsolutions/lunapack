---
status: accepted
date: 2026-08-27
decision-makers: LunaPack maintainers
---

# ADR-0054: Use Semantic CLI Presentation and Explicit Defaults

## Context and Problem Statement

Lifecycle output mixed commands, warnings, instructions, and success messages
without enough visual hierarchy. Script consent also selected yes on Enter,
while safe parameter defaults could not be declared by pack authors.

## Decision Drivers

- Keep executable consent conservative and distinct from instructions.
- Preserve readable output on terminals with light or dark themes.
- Make long-running command completion and duration easy to identify.
- Let consumers accept author-provided typed parameter defaults with Enter.
- Preserve existing parameter precedence and lifecycle trust boundaries.

## Considered Options

- Add semantic Spectre.Console styles and typed defaults.
- Keep plain output and affirmative script consent.
- Build a terminal-theme-specific presentation layer.

## Decision Outcome

Chosen option: "Add semantic Spectre.Console styles and typed defaults",
because ANSI semantic colors and text emphasis provide hierarchy without
assuming a terminal background, while explicit defaults reduce input without
weakening script consent.

Successful actions use green, guidance and instruction headings use cyan, and
warnings and errors retain their existing diagnostic colors. Lifecycle output
starts on a separate line. Script approval shows the effective command and
optional description, then defaults to no. Instructions remain non-executable,
display automatically unless skipped, omit their H1 title, and render bounded
bold, italic, inline-code, fenced-code, and link presentation.

Parameter declarations may define a typed `default`. Required parameters still
prompt and offer that value; optional parameters bind it automatically. Explicit
arguments, composite bindings, and project variables retain their existing
precedence. Catalog and pack lifecycle summaries include elapsed duration;
install and update success lines include selected versions.

This decision refines the presentation rule in ADR-0053. It does not change
ordered hook planning, trust, snapshot, dispatch, or rollback behavior.

### Consequences

- Good, because prompts distinguish executable consent from safe instruction display.
- Good, because completion, guidance, and instruction structure scan consistently.
- Good, because defaults remain type-checked and do not alter binding precedence.
- Bad, because redirected output does not retain color or text emphasis.
- Bad, because Markdown rendering intentionally supports only a bounded subset.

### Confirmation

Console, instruction, authorization, parameter, schema, catalog, and lifecycle
tests verify prompt defaults, rendered transcripts, typed binding, selected
versions, and timed summaries.

## More Information

See [ADR-0030](0030-use-spectre-console-for-cli-output.md),
[ADR-0031](0031-require-pack-attribution-and-interactive-parameters.md), and
[ADR-0053](0053-unify-ordered-lifecycle-hooks.md).
