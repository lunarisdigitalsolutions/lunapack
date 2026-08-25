---
status: accepted
date: 2026-08-21
decision-makers: Lunaris Engineering
---

# ADR-0035: Skip Installed Roots In Multi-Pack Installs

## Context and Problem Statement

Multi-reference installation stopped at an already installed root, preventing
later requested roots from being applied. Explicit version requests also gave
no guidance when the pack existed but the requested release did not.

## Decision Drivers

- Preserve ordered installation of independently requested roots.
- Avoid reporting an unchanged configured root as a command failure.
- Give actionable release information for explicit version misses.

## Considered Options

- Fail multi-reference installation at an installed root.
- Warn and skip installed roots in multi-reference installation.
- Reinstall an existing root silently.

## Decision Outcome

Chosen option: "Warn and skip installed roots in multi-reference installation",
because it lets later roots proceed without rewriting existing root state.

### Consequences

- A multi-reference install warns and skips an already configured requested root.
- A single-reference duplicate install retains its existing failure behavior.
- An explicit unavailable version suggests the latest cataloged version when
  the pack ID exists.
- Existing-root conflicts at different resolved versions still fail before
  managed files change.

### Confirmation

CLI lifecycle tests cover warning-and-continue behavior and latest-version
suggestions for explicit unavailable installs.

## More Information

This refines incremental root processing in
[ADR-0032](0032-apply-lifecycle-roots-incrementally.md).
