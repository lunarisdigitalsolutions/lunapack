---
status: accepted
date: 2026-08-21
decision-makers: LunaPack maintainers
---

# ADR-0030: Use Spectre.Console for CLI Output

## Context and Problem Statement

LunaPack needs one output boundary for command results, diagnostics, progress,
and interactive confirmations. The earlier Serilog stderr sink split console
behavior across logging and direct `Console` calls.

## Decision Outcome

Chosen option: "Use an invocation-scoped Spectre.Console boundary", because it
formats command output, colored diagnostics, status spinners, and prompts with
one injected dependency.

### Consequences

- `--log-level` and `-ll` accept only lower-case levels.
- Info messages are plain; verbose, debug, warning, and error messages have
  colored level prefixes.
- All LunaPack-owned output uses standard output through `IAnsiConsole`.
- Unit tests inject a no-color, silent `IAnsiConsole`.
- Serilog and its console sink are removed from the CLI dependency graph.

### Confirmation

Parser, unit, integration, and Release-build checks validate level filtering,
prompt composition, managed-file diagnostics, and console output.

## More Information

- [Runtime contracts](../runtime.md)
- [CLI commands](../../../developer/cli/commands.md)
