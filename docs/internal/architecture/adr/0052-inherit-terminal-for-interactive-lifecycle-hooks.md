---
status: accepted
date: 2026-08-27
decision-makers: LunaPack maintainers
---

# ADR-0052: Inherit the Terminal for Interactive Lifecycle Hooks

## Context and Problem Statement

Lifecycle hooks can invoke interactive tools such as `npm init`. LunaPack
previously redirected hook output and emitted it only after process exit. The
child inherited input, but its prompts remained buffered and invisible, making
interactive commands appear to hang.

## Decision Drivers

- Show child prompts before the child waits for input.
- Preserve native terminal behavior for interactive tools.
- Keep bounded, sanitized output for automation and noninteractive sessions.
- Avoid implementing terminal emulation or prompt-protocol assumptions.

## Considered Options

- Inherit terminal streams only when LunaPack is interactive.
- Always inherit terminal streams.
- Relay redirected streams while forwarding input.

## Decision Outcome

Chosen option: "Inherit terminal streams only when LunaPack is interactive",
because interactive hooks need native terminal semantics while automated runs
still need bounded output capture.

Interactive hook processes inherit standard input, output, and error and may
render prompts directly. Noninteractive hook processes retain redirected,
bounded output and must not require user input.

### Consequences

- Good, because interactive tools can display prompts and read answers normally.
- Good, because terminal capabilities such as colors and cursor control remain
  available to child processes.
- Good, because noninteractive output remains bounded and sanitized.
- Bad, because interactive child output bypasses LunaPack log formatting and
  sanitization.
- Bad, because noninteractive hooks that prompt can still block and remain pack
  author errors.

### Confirmation

Unit tests verify that interactive process start information inherits all three
standard streams. Lifecycle executor tests verify that noninteractive failures
and cancellation retain existing behavior.

## Pros and Cons of the Options

### Inherit Streams Only When Interactive

- Good, because behavior follows the capabilities of the invoking session.
- Bad, because output handling differs between interactive and automated runs.

### Always Inherit Streams

- Good, because process I/O behavior is uniform.
- Bad, because automation loses bounded output capture and sanitization.

### Relay Redirected Streams and Forward Input

- Good, because LunaPack could retain output control while prompts are visible.
- Bad, because reliable terminal emulation is platform-specific and substantially
  more complex than stream forwarding.

## More Information

See [ADR-0040](0040-secure-lifecycle-scripts-with-scoped-trust.md) and
[Lifecycle Script Safety](../../development/lifecycle-script-safety.md).
