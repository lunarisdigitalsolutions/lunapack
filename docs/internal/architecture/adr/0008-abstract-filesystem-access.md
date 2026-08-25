---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0008: Abstract Filesystem Access

## Context and Problem Statement

The CLI performs filesystem operations for manifests, pack discovery, and
managed files. Direct calls to static filesystem APIs make isolated unit tests
depend on local disk state and obscure which tests require real filesystem
behavior.

## Decision Drivers

- Isolate filesystem-owning services in unit tests.
- Keep the real filesystem covered by integration tests.
- Make filesystem dependencies explicit at the CLI composition boundary.

## Considered Options

- Inject `IFileSystem` through System.IO.Abstractions.
- Use static `File`, `Directory`, and `Path` calls.
- Add test-only wrappers around static APIs.

## Decision Outcome

Chosen option: "Inject `IFileSystem` through System.IO.Abstractions", because
it gives filesystem-owning services an explicit, substitutable dependency while
retaining real-filesystem integration coverage.

### Consequences

- Good, because filesystem behavior has an explicit dependency boundary and can be tested without disk access.
- Good, because test runners can select real-filesystem integration tests using their `FileSystem` property.
- Bad, because production composition must construct and pass the concrete `FileSystem` at the CLI entry point.

### Confirmation

Unit tests use `MockFileSystem` for isolated paths, while integration tests
declare `FileSystem=Real` and run the built CLI against the real filesystem.

## Pros and Cons of the Options

### Inject `IFileSystem` through System.IO.Abstractions

- Good, because filesystem behavior can be substituted in isolated tests.
- Bad, because services require an injected abstraction.

### Use static `File`, `Directory`, and `Path` calls

- Good, because filesystem calls remain direct and familiar.
- Bad, because they prevent substituting filesystem behavior in isolated tests.

### Add test-only wrappers around static APIs

- Good, because some tests could avoid direct disk access.
- Bad, because production dependencies would remain implicit and abstractions would be duplicated.

## More Information

Use System.IO.Abstractions and inject `IFileSystem` into filesystem-owning
services. Unit tests use the abstraction and may use `MockFileSystem` for
isolated behavior. Integration tests continue to exercise the real filesystem
and declare TUnit property `FileSystem=Real`.
