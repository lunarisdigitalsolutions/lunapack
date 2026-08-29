---
status: accepted
date: 2026-08-27
decision-makers: [LunaPack maintainers]
---

# ADR-0062: Organize CLI source by feature

## Context and Problem Statement

The CLI source mixed domain behavior in broad or flat locations. Finding a
workflow required knowing type names before knowing its owner, and namespaces
did not consistently communicate architecture boundaries.

## Decision Drivers

- Make related command, policy, model, and service types easy to find together.
- Make namespaces communicate ownership without catch-all technical layers.
- Keep cross-cutting boundaries explicit and keep `Program` as composition root.

## Considered Options

- Organize source by feature with matching namespaces.
- Organize source by technical type such as handlers, models, and interfaces.
- Keep a mostly flat source directory and namespace.

## Decision Outcome

Chosen option: "Organize source by feature with matching namespaces", because a
feature usually changes as one unit and should be understandable from one
cohesive location.

Place each production type under its narrowest owning feature and make its
namespace match that directory. Keep genuinely shared behavior under a named
cross-cutting owner such as application command execution, project paths, or
serialization. Do not create catch-all `Interfaces`, `Models`, or `Helpers`
directories. Keep dependency construction in `Program`.

### Consequences

- Good, because source location and namespace identify feature ownership.
- Good, because related behavior can evolve without spreading technical layers.
- Bad, because moves require coordinated namespace, test import, and source
  generation updates.

### Confirmation

Review production paths and namespaces together. A Release solution build
confirms project inclusion, imports, and source-generation registrations after
moves.

## Pros and Cons of the Options

### Organize source by feature with matching namespaces

- Good, because related behavior stays close and ownership remains visible.
- Bad, because some cross-feature boundaries require deliberate placement.

### Organize source by technical type

- Good, because files with similar mechanics share a directory.
- Bad, because one feature change crosses several unrelated directories.

### Keep a flat source directory and namespace

- Good, because moves and imports are minimal.
- Bad, because ownership becomes implicit as the CLI grows.

## More Information

Related: [C# coding guidelines](../../development/coding-guidelines/csharp.md)
and [clean-code principles](../../development/coding-guidelines/clean-code.md).
