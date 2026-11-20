---
status: accepted
date: 2026-11-20
decision-makers:
  - Lunaris Engineering
---

# ADR-0097: Show lifecycle consent details at every log level

## Context and Problem Statement

Lifecycle consent authorizes pack-controlled code to run with the invoking
user's permissions. The prompt must identify the pack and exact resolved
command, arguments, and optional description so the user can make an informed
decision. Filtering these details at warning or error log levels can leave the
confirmation without the context needed to review the requested execution.

## Decision Drivers

- Keep consent informed regardless of the configured minimum log level.
- Preserve safe rendering of pack-controlled text.
- Keep ordinary informational output subject to log-level filtering.

## Considered Options

- Render consent details through the standard level-filtered logging methods.
- Write sanitized consent details directly through the console boundary.
- Ignore the configured level for all informational output.

## Decision Outcome

Chosen option: "Write sanitized consent details directly through the console
boundary," because consent details are part of the authorization decision, not
optional diagnostic output.

### Consequences

- Good, because the user sees the pack ID, resolved command and arguments, and
  optional description before confirmation at every log level.
- Good, because details still pass through the console's output sanitization.
- Bad, because consent details remain visible when informational output is
  otherwise suppressed.
- Good, because other informational output retains its existing filtering.

### Confirmation

Security tests verify consent details at warning and error log levels, and
OpenSpec defines the required consent content.

## Pros and Cons of the Options

### Render consent details through the standard level-filtered logging methods

- Good, because it follows ordinary log-level filtering.
- Bad, because warning and error levels can hide information needed to decide
  whether to authorize code execution.

### Write sanitized consent details directly through the console boundary

- Good, because authorization remains informed at every log level.
- Bad, because consent details are displayed even when informational output is
  suppressed.

### Ignore the configured level for all informational output

- Good, because no informational details are hidden.
- Bad, because unrelated informational output becomes noisy at restricted
  levels.

## More Information

Public guidance is in `docs/developer/cli/trust-and-scripts.md`. The lifecycle
consent contract is maintained in `openspec/specs/local-pack-lifecycle/spec.md`.
