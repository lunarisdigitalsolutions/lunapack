---
status: accepted
date: 2026-08-25
decision-makers: [Lunaris Digital Solutions]
---

# ADR-0047: Retain Lock Evidence After Source Removal

## Context and Problem Statement

Removing a configured source must not implicitly uninstall packs or discard
their provenance. Source names also participate in project trust, so retaining
name-bound trust after removal could authorize a different source later.

## Decision Drivers

- Keep source management separate from pack lifecycle mutation.
- Preserve audit and safe-uninstall evidence for installed packs.
- Prevent trust transfer when a source name is reused.
- Keep source, trust, and project-state writes atomic.

## Considered Options

- Remove configuration and bound trust while retaining lock evidence.
- Reject removal while installed packs reference the source.
- Remove installed packs together with the source.
- Remove source configuration but retain project trust.

## Decision Outcome

Chosen option: "Remove configuration and bound trust while retaining lock
evidence", because it preserves installed ownership without extending trust.

`luna sources remove <name>` atomically removes the configured source and
project source and pack trust bound to its name. Requested roots, resolved lock
records, and managed files remain. Project-state loading accepts lock identities
whose source is no longer configured. General state writes retain strict
source-matching validation; source and uninstall writes use the narrow
unavailable-source path so removal, source reuse, and safe uninstall remain
possible.

### Consequences

- Good, because source removal cannot silently delete managed content.
- Good, because audit and uninstall retain immutable provenance and ownership.
- Good, because reusing a name does not inherit project trust.
- Bad, because update requires re-adding the original source or explicitly
  confirming a source switch.

### Confirmation

Source command tests verify atomic removal, trust revocation, unknown-name
failure, lock and managed-file retention, last-source guidance, and source-name
reuse behavior. Existing state-store tests retain strict general-write checks.

## Pros and Cons of the Options

### Remove configuration and bound trust while retaining lock evidence

- Good, because it separates source availability from installed state.
- Bad, because valid lock state can reference an unavailable source.

### Reject removal while installed packs reference the source

- Good, because all lock sources remain available.
- Bad, because source cleanup becomes coupled to uninstall order.

### Remove installed packs together with the source

- Good, because no unavailable provenance remains.
- Bad, because a configuration command would mutate consumer files.

### Remove source configuration but retain project trust

- Good, because restoring the same source requires less setup.
- Bad, because a rebound name could inherit authority.

## More Information

- [Lifecycle script safety](../../development/lifecycle-script-safety.md)
- [Add a pack source](../../../developer/sources.md)
- [ADR-0040](0040-secure-lifecycle-scripts-with-scoped-trust.md)
