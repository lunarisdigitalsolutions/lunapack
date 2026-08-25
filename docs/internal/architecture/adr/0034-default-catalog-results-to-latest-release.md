---
status: accepted
date: 2026-08-21
decision-makers: Lunaris Engineering
---

# ADR-0034: Default Catalog Results To Latest Release

## Context and Problem Statement

Discover listed the latest release while search listed three releases by
default. The differing defaults made catalog output harder to scan and search
results unexpectedly repeated package IDs.

## Decision Drivers

- Give discover and search the same default release scope.
- Keep catalog tables concise and easy to compare.
- Retain bounded access to recent release history.

## Considered Options

- Retain the former latest-only discover and three-version search defaults.
- Default both commands to the latest release and offer bounded history.
- Display every available release by default.

## Decision Outcome

Chosen option: "Default both commands to the latest release and offer bounded
history", because it gives package IDs one primary result while preserving a
deliberate way to inspect recent releases.

### Consequences

- Discover and search display one latest distinct release per pack by default.
- Both commands accept `--versions` or `-v` from one through 10.
- Tables keep Pack and Version in separate columns.

### Confirmation

CLI application tests cover default latest-only output, version overrides, and
the maximum version-count rejection for both commands.

## More Information

The catalog specification defines ordering and metadata relevance behavior.
