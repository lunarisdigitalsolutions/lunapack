---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0007: Standardize .NET Build and Schema Tooling

## Context and Problem Statement

The CLI needs reproducible SDK selection, package restore, line-ending rules,
and JSON Schema validation compatible with the repository license policy.

## Decision Drivers

- Make local and CI builds reproducible.
- Keep generated artifacts out of source control.
- Validate schemas with a dependency allowed by the repository license policy.
- Publish schemas using a compatible JSON Schema vocabulary.

## Considered Options

- Pin the .NET SDK and use NJsonSchema with Draft 7 schemas.
- Use JsonSchema.Net.
- Allow floating SDK selection.

## Decision Outcome

Chosen option: "Pin the .NET SDK and use NJsonSchema with Draft 7 schemas",
because it satisfies reproducibility, artifact-management, compatibility, and
approved-license requirements.

### Consequences

- Good, because developers and CI use the same .NET 10 SDK patch.
- Good, because package restores are locked in CI and generated artifacts stay out of source control.
- Good, because the validator meets the approved MIT or Apache-2.0 license requirement.
- Bad, because schema documents use Draft 7 rather than newer vocabulary such as `$defs` and `const`.

### Confirmation

`global.json`, shared MSBuild configuration, and schema-validation tests verify
the SDK, deterministic build, restore, and Draft 7 schema contract.

## Pros and Cons of the Options

### Pin the .NET SDK and use NJsonSchema with Draft 7 schemas

- Good, because it provides reproducible builds and an approved validator.
- Bad, because the schema vocabulary excludes newer JSON Schema features.

### Use JsonSchema.Net

- Good, because it is a dedicated JSON Schema implementation.
- Bad, because its license is not approved for this repository.

### Allow floating SDK selection

- Good, because developers can use newer local SDK patches immediately.
- Bad, because it reduces reproducibility across developer machines and CI.

## More Information

Pin .NET SDK `10.0.302` in `global.json`, enable deterministic builds and
package lock files in shared MSBuild configuration, and add shared EditorConfig,
Git attributes, and .NET artifact ignore rules. Use MIT-licensed NJsonSchema
for JSON Schema validation. Publish version-1 schemas with the supported JSON
Schema Draft 7 vocabulary.
