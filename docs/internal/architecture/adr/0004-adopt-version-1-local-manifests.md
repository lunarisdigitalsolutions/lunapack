---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0004: Adopt Version-1 Local Manifests

## Context and Problem Statement

The first CLI slice needs portable, machine-readable project and pack contracts
without introducing lock files or remote catalog behavior.

## Decision Drivers

- Publish portable, machine-readable project and pack contracts.
- Validate the first CLI slice against stable, local schemas.
- Exclude lock-file and remote-catalog behavior from the initial scope.

## Considered Options

- Use version-1 local manifests and JSON Schemas.
- Use hand-written validation only.
- Introduce a lock file in the first slice.

## Decision Outcome

Chosen option: "Use version-1 local manifests and JSON Schemas", because it
provides an explicit, portable, schema-validated local contract without adding
out-of-scope resolution behavior.

### Consequences

- Good, because the initial runtime has an explicit, schema-validated local contract.
- Bad, because planning references to `lunapack.yaml` are incompatible with the implemented `lunapack.yml` contract.

### Confirmation

Schema validation verifies `lunapack.yml` and `pack.yml` against the published
version-1 schemas, while reviews reject lock-file and remote-catalog behavior
from this slice.

## Pros and Cons of the Options

### Use version-1 local manifests and JSON Schemas

- Good, because the CLI publishes portable contracts.
- Bad, because the version-1 schema vocabulary constrains later evolution.

### Use hand-written validation only

- Good, because it can be tailored directly to current CLI code.
- Bad, because it would not publish a portable contract.

### Introduce a lock file in the first slice

- Good, because it could prepare for future dependency resolution.
- Bad, because resolution is outside the first slice.

## More Information

Use `lunapack.yml` and `pack.yml` with version-1 JSON Schemas in
`projects/schema/`. Store local source and managed-file ownership state in the
project manifest.

- [Version-1 schemas](../../../../projects/schema/)
