---
status: accepted
date: 2026-08-26
decision-makers: LunaPack maintainers
---

# ADR-0048: Separate Pack Authoring Validity From Publication Readiness

## Context and Problem Statement

Incremental CLI authoring must never persist an invalid `pack.yml`. Requiring
attribution and content in every schema-valid manifest prevents `luna pack init`
from creating a safe starting point before authors add those values.

This decision supersedes the attribution requirement in
[ADR-0031](0031-require-pack-attribution-and-interactive-parameters.md).
Interactive parameter behavior from that record remains unchanged.

## Decision Drivers

- Every authoring command must leave a schema-valid manifest.
- Authors need a small starting document that can grow incrementally.
- Existing complete manifests must remain valid.
- Distribution policy must remain separable from document structure.

## Considered Options

- Permit identity-only manifests and enforce publication readiness separately.
- Persist an invalid draft until the first content entry is added.
- Introduce a separate draft-manifest format.

## Decision Outcome

Chosen option: "Permit identity-only manifests and enforce publication readiness
separately", because one schema-valid `pack.yml` can represent every authoring
stage without creating a second contract.

### Consequences

- `pack.yml` requires only non-empty `id` and semantic `version`.
- Empty content collections are valid.
- `name`, `author`, `homepage`, and `license` are optional but constrained when
  present.
- Catalogs can discover identity-only manifests; future publication tooling must
  own any stricter readiness policy.
- Typed CLI writes preserve modeled values but may normalize YAML formatting.

### Confirmation

Schema and model tests cover identity-only, complete legacy, and invalid
manifests. Authoring command tests confirm initialization and every mutation
validate before atomic replacement.

## Pros and Cons of the Options

### Permit identity-only manifests

- Good, because every incremental state uses the published contract.
- Good, because existing manifests need no migration.
- Bad, because schema validity alone no longer means publication readiness.

### Persist an invalid draft

- Good, because publication requirements remain encoded in one schema.
- Bad, because it violates manifest-safe authoring and breaks normal validation.

### Introduce a draft format

- Good, because draft and publication rules remain explicit.
- Bad, because authors and tooling must migrate between two document contracts.

## More Information

- [Pack authoring specification](../../../../openspec/changes/add-pack-authoring-commands/specs/pack-authoring/spec.md)
- [Pack manifest reference](../../../developer/packs/reference/manifest.md)
