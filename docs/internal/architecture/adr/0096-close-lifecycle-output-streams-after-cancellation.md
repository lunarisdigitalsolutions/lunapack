---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0096: Close redirected lifecycle output after cancellation

## Context and Problem Statement

Noninteractive lifecycle hooks use redirected output streams. After cancellation
stops the hook process tree, a detached descendant can retain an inherited
stream handle and prevent the CLI from observing end-of-file. Waiting for that
stream can block cancellation completion and lifecycle history recording.

## Decision Drivers

- Complete cancellation without waiting for detached descendants.
- Preserve output already read from redirected streams.
- Keep successful execution and interactive stream behavior unchanged.
- Record the canceled attempt after process termination.

## Considered Options

- Continue waiting for redirected output streams to reach end-of-file.
- Cancel pending reads and close redirected streams after stopping the process
  tree.
- Add a bounded post-cancellation drain period before closing streams.

## Decision Outcome

Chosen option: "Cancel pending reads and close redirected streams after
stopping the process tree," because cancellation must not depend on detached
processes closing inherited handles.

### Consequences

- Good, because cancellation and history recording complete without waiting for
  detached descendants.
- Good, because output already read remains available for normal CLI rendering.
- Bad, because output not yet read when cancellation closes the streams may be
  discarded.
- Good, because successful execution and interactive hooks retain existing
  output behavior.

### Confirmation

Unit tests verify cancellation returns promptly when a detached descendant
retains a redirected output handle. Lifecycle documentation and the OpenSpec
contract describe the resulting output behavior.

## Pros and Cons of the Options

### Continue waiting for redirected output streams to reach end-of-file

- Good, because all output is preserved.
- Bad, because a detached descendant can delay or indefinitely block
  cancellation.

### Cancel pending reads and close redirected streams after stopping the process tree

- Good, because cancellation does not depend on descendant exit.
- Bad, because unread output may be lost.

### Add a bounded post-cancellation drain period before closing streams

- Good, because some output written during shutdown may still be captured.
- Bad, because cancellation latency depends on an arbitrary timeout.

## More Information

The public behavior is documented in
`docs/developer/cli/trust-and-scripts.md`. The lifecycle hook contract is
maintained in `openspec/specs/local-pack-lifecycle/spec.md`.
