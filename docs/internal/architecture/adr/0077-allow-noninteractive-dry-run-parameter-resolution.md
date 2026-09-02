---
status: accepted
date: 2026-09-01
decision-makers: LunaPack maintainers
---

# ADR-0077: Allow Noninteractive Dry-Run Parameter Resolution

## Context and Problem Statement

ADR-0076 requires install and update dry runs to prompt for each configurable
parameter on active composite paths. That produces accurate conditional plans
for interactive users, but prevents unattended automation from previewing packs
whose defaults and supplied values already provide a complete parameter set.

## Decision Drivers

- Preserve parameter-aware plans as the default dry-run behavior.
- Support unattended previews without weakening required-parameter validation.
- Keep parameter resolution consistent between interactive and noninteractive
  previews.
- Make contradictory prompt policies fail before lifecycle resolution.

## Considered Options

- Always prompt during dry runs.
- Infer noninteractive behavior from redirected input.
- Add an explicit dry-run-only option that suppresses parameter prompts.

## Decision Outcome

Chosen option: "Add an explicit dry-run-only option that suppresses parameter
prompts," because automation can state its intent without changing interactive
defaults or relying on terminal detection.

Install and update accept `--skip-parameters` only with `--dry-run`. The option
is mutually exclusive with `--prompt-parameters`. It disables parameter prompts
but does not disable declared defaults, variables, composite bindings, explicit
`--parameter` values, active-path selection, or required-parameter validation.
An unresolved required parameter still fails preflight.

### Consequences

- Existing dry runs continue prompting by default.
- Automation can produce conditional plans without reading standard input.
- A skipped prompt is not equivalent to omitting parameter resolution.
- Conflicting or non-dry-run use fails before lifecycle work starts.
- ADR-0076 remains authoritative for active-path traversal and prompt ordering;
  this decision adds an explicit noninteractive exception.

### Confirmation

Command tests verify install and update dry runs suppress prompts while retaining
selected defaults and explicit values. Validation tests reject conflicting prompt
options and use outside dry runs. Help-contract tests require the option on both
commands, and lifecycle tests retain unresolved required and active
`requiredWhen` parameter failures.
