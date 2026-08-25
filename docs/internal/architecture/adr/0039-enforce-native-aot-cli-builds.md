---
status: accepted
date: 2026-08-21
decision-makers: LunaPack maintainers
---

# ADR-0039: Enforce Native AOT CLI Builds

## Context and Problem Statement

Runtime NJsonSchema validation required reflection and dynamic code, so it was
incompatible with Native AOT. The CLI now deserializes manifests into typed
models and validates their schema constraints with local code. This removes the
incompatible dependency path while preserving schema documents as the public
contract reference.

## Decision Drivers

- Publish a native executable that works for every supported CLI target.
- Fail builds on Native AOT compiler and linker incompatibilities.
- Preserve manifest validation without reflection-based runtime dependencies.
- Keep Native AOT validation aligned with every supported release target.

## Considered Options

- Continue self-contained managed publishing.
- Suppress Native AOT diagnostics for NJsonSchema dependencies.
- Replace runtime schema validation with typed validators and require Native AOT publishing.

## Decision Outcome

Chosen option: "Replace runtime schema validation with typed validators and require Native AOT publishing", because it removes the incompatible dynamic-code path and lets the Native AOT compiler validate the distributed executable.

The CLI project enables Native AOT compatibility analysis. Local build scripts,
VS Code tasks, pull request validation, and release workflows publish Native
AOT binaries for `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and
`osx-arm64`. Each target publishes on a matching native runner.

### Consequences

- Good, because normal build and release paths exercise the actual Native AOT compiler.
- Good, because manifest validation has explicit, testable typed rules.
- Good, because the CLI no longer ships NJsonSchema, Newtonsoft.Json, or dynamic schema-validation code.
- Bad, because model and JSON-schema changes must update the typed validator and its tests together.
- Bad, because dependencies without Native AOT metadata cannot use strict reference-metadata verification; the required publish remains the compatibility gate.

### Confirmation

Run `./build.ps1 -Os <win|linux|osx> -Platform <x64|arm64> -Publish` for a
supported host/runtime pair. The command must complete a Native AOT publish. CI
runs the same publish for each supported target, and validator unit tests cover
accepted and rejected manifest state.

## Pros and Cons of the Options

### Continue self-contained managed publishing

- Good, because no validation migration is required.
- Bad, because it does not provide Native AOT startup and deployment benefits.

### Suppress Native AOT diagnostics for NJsonSchema dependencies

- Good, because publishing can appear to succeed with minimal source change.
- Bad, because dynamic and reflection-dependent runtime behavior is unsupported by Native AOT.

### Replace runtime schema validation with typed validators and require Native AOT publishing

- Good, because validation behavior is explicit and compatible with Native AOT.
- Bad, because schema constraints are maintained in two representations.
