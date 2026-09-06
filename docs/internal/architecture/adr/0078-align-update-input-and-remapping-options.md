---
status: accepted
date: 2026-09-01
decision-makers:
  - Lunaris Engineering
---

# ADR-0078: Align Update Input and Remapping Options

## Context and Problem Statement

Install accepted explicit parameters, project-variable controls, and target
remappings, while update exposed only parameter prompting. Consumers could not
reproduce an installation's configurable inputs when changing a release or
intentionally relocate managed targets during an update.

## Decision Drivers

- Keep parameter precedence and `requiredWhen` validation consistent across
  installation and update.
- Let automation provide values without interactive prompts.
- Preserve installed destinations and ownership unless a named update explicitly
  remaps a target.
- Avoid ambiguous remapping persistence when updating multiple roots.

## Considered Options

- Keep update limited to parameter prompts.
- Add only explicit parameter and variable inputs.
- Align parameter, variable, and remapping inputs where update semantics exist.

## Decision Outcome

Chosen option: "Align parameter, variable, and remapping inputs where update
semantics exist," because the same resolved graph needs the same configurable
input controls while installation-only ownership decisions remain distinct.

Update accepts explicit `--parameter` values, `--no-variables`, repeatable
`--skip-variable`, repeatable `--remap-directory` and `--remap-file`, and
`--save-remap`. Parameter inputs and variables use installation precedence and
validation, including active `requiredWhen` evaluation. The `-p` alias remains
reserved for update-all selection, so update parameter values use the long form.

Command remappings require exactly one pack reference and override retained lock
targets for that invocation. `--save-remap` requires a command remapping and
stores it on the selected root. Update retains the installed destination and
does not expose install-only adoption behavior.

### Consequences

- Install and update can resolve the same graph from equivalent consumer input.
- Named updates can relocate existing managed targets transactionally.
- Update-all can use parameter and variable controls but cannot apply ambiguous
  command remappings.
- Update help differs from install where command semantics or aliases differ.

### Confirmation

Help-contract tests require the shared options. Update command tests verify
explicit parameters, full and selective variable suppression, active
`requiredWhen` failures, transient remapping, saved remapping, and invalid option
combinations. Release builds and lifecycle tests confirm existing update paths
remain compatible.

## Pros and Cons of the Options

### Keep Update Limited to Parameter Prompts

- Good, because the update command remains small.
- Bad, because noninteractive consumers cannot reproduce installation inputs.

### Add Only Explicit Parameter and Variable Inputs

- Good, because update can resolve conditional content consistently.
- Bad, because consumers still cannot intentionally relocate managed targets.

### Align Inputs Where Update Semantics Exist

- Good, because install and update share resolution behavior.
- Good, because remapping remains explicit and attributable.
- Bad, because update gains more validation and help surface.

## More Information

- [ADR-0076](0076-prompt-parameters-across-active-composite-paths.md)
- [ADR-0077](0077-allow-noninteractive-dry-run-parameter-resolution.md)
- [Local pack lifecycle specification](../../../../openspec/specs/local-pack-lifecycle/spec.md)
