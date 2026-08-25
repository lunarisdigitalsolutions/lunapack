---
status: accepted
date: 2026-08-18
decision-makers: [Lunaris Digital Solutions]
---

# ADR-0020: Use plural sources CLI command

## Context and Problem Statement

The command that manages a collection of configured pack sources was named
`source`. Consumers need to inspect every configured source and its properties.

## Decision Drivers

- Match the command name to its collection-level behavior.
- Provide direct visibility into persisted source configuration.
- Keep output concise and script-readable.

## Considered Options

- Rename the command to `sources` and add `sources list`.
- Retain `source` and add `source list`.
- Keep the singular command as an alias.

## Decision Outcome

Chosen option: "Rename the command to `sources` and add `sources list`", because
the command manages source collections and the new list subcommand exposes the
configured records without a compatibility alias.

### Consequences

- Good, because command language matches its collection-level scope.
- Good, because consumers can inspect source type and configured properties.
- Bad, because existing `lunapack source` invocations must migrate to `lunapack sources`.

### Confirmation

Focused formatter and CLI integration tests confirm the source-list output and
the migrated command invocation.

## Pros and Cons of the Options

### Rename the command to `sources` and add `sources list`

- Good, because it is clear and consistent with the managed collection.
- Bad, because it changes the CLI contract.

### Retain `source` and add `source list`

- Good, because existing invocations continue to work.
- Bad, because the singular name misrepresents collection behavior.

### Keep the singular command as an alias

- Good, because it allows staged migration.
- Bad, because it extends the compatibility surface without a versioning need.

## More Information

Developer command documentation defines the output format and migration target.
