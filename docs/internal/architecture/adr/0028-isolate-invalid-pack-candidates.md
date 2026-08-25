---
status: accepted
date: 2026-08-21
decision-makers: LunaPack maintainers
---

# ADR-0028: Isolate Invalid Pack Candidates During Catalog Browsing

## Context and Problem Statement

A source can contain usable and malformed packs. Rejecting a whole source for
one missing selected file blocks discovery, installation, and update of valid
releases.

## Decision Outcome

Chosen option: "Skip invalid candidates and provide explicit validation",
because catalog availability and author diagnostics are separate workflows.

### Consequences

- Valid packs remain available beside malformed siblings.
- `validate` reports manifest and selected-source-file issues.
- Authors must run validation to inspect skipped candidates.

### Confirmation

Catalog tests prove a valid candidate remains discoverable beside an invalid
candidate, and validation tests report missing selected source files.

## More Information

- [Runtime contracts](../runtime.md)
- [CLI commands](../../../developer/cli/commands.md)
