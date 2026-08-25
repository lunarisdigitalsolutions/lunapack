---
status: accepted
date: 2026-08-21
decision-makers: [Lunaris engineering]
---

# ADR-0029: Provide CLI Option Shorthand Aliases

## Context and Problem Statement

Luna commands exposed descriptive long options but most required verbose input
for frequent interactive use. Shorthand syntax is part of the public CLI
contract and must remain consistent as commands evolve.

## Decision Drivers

- Reduce common command input without sacrificing clear documentation.
- Preserve long options and existing scripts.
- Avoid ambiguous aliases within a command scope.

## Considered Options

- Provide stable, command-scoped shorthand aliases.
- Retain long options only.
- Introduce implicit prefix abbreviation.

## Decision Outcome

Chosen option: "Provide stable, command-scoped shorthand aliases," because it
improves interactive use while keeping parsing explicit and backward compatible.

### Consequences

- Each CLI option has a documented alias, such as `-w` for `--workspace` and
  `-ll` for `--log-level`.
- Long options remain supported.
- Identical aliases may represent different options only in separate command
  scopes.
- New options require a documented non-conflicting shorthand alias.

### Confirmation

CLI workflow tests exercise aliases for global, source, install, and update
options. The command reference lists every alias.

## Pros and Cons of the Options

### Provide Stable, Command-Scoped Shorthand Aliases

- Good, because common commands require less input.
- Good, because explicit aliases avoid parser guesswork.
- Bad, because aliases consume a limited command-scoped namespace.

### Retain Long Options Only

- Good, because no alias mapping needs maintenance.
- Bad, because frequent commands remain unnecessarily verbose.

### Introduce Implicit Prefix Abbreviation

- Good, because it requires no per-option declarations.
- Bad, because future options can make existing abbreviations ambiguous.

## More Information

See the [command reference](../../../developer/cli/commands.md) for the current
public alias mapping.
