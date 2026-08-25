---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0013: Adopt System.CommandLine For CLI Parsing

## Context and Problem Statement

The initial CLI interpreted `string[]` arguments with a switch expression. As
commands and options grow, that approach duplicates parser validation, help,
and error-reporting behavior in application code. The CLI needs a maintained,
permissively licensed parser while preserving the testable command-application
boundary.

## Decision Drivers

- Provide consistent command syntax, validation, help, and exit behavior.
- Keep command handlers thin and application operations testable without a
  process.
- Use an actively maintained, widely adopted package with a permissive license.

## Considered Options

- Use System.CommandLine 2.0.10.
- Continue custom `string[]` parsing.
- Adopt another third-party command-line parser.

## Decision Outcome

Chosen option: "Use System.CommandLine 2.0.10", because its stable API
provides command composition, argument validation, and standard help behavior
without moving filesystem or pack operations out of `CliApplication`.

### Consequences

- Good, because parser behavior is consistent as commands grow.
- Good, because command syntax is declared alongside thin delegates to
  testable application operations.
- Bad, because the CLI has a new package dependency that requires regular
  maintenance and lockfile review.

### Confirmation

`CliApplicationTests` verifies command success and parser failures. The
integration suite verifies the built process can complete the pack lifecycle.

## Pros and Cons of the Options

### Use System.CommandLine 2.0.10

- Good, because it is a stable, maintained, MIT-licensed command-line library.
- Bad, because its API and dependency graph must be reviewed during upgrades.

### Continue Custom `string[]` Parsing

- Good, because it adds no dependency.
- Bad, because each command expands custom parsing and help behavior.

### Adopt Another Third-Party Command-Line Parser

- Good, because alternative API shapes may suit specialized CLI needs.
- Bad, because it provides no identified benefit over the Microsoft-maintained
  package.

## More Information

Package selection and future upgrades follow the
[adding packages guidance](../../development/package-management.md).
