---
status: accepted
date: 2026-08-29
decision-makers: [LunaPack maintainers]
---

# ADR-0063: Organize CLI tests by feature and scenario

## Context and Problem Statement

CLI production code is organized by feature, but its test projects remained
mostly flat. Test location therefore did not communicate the production owner
or, for broader integration and security coverage, the exercised scenario.

## Decision Drivers

- Make production behavior and its focused tests easy to navigate together.
- Keep broad integration and security scenarios discoverable without forcing a
  false one-to-one mapping to production types.
- Separate reusable test infrastructure from behavior-focused tests.
- Preserve clear test names and diagnostics when considering parameterization.

## Considered Options

- Mirror feature ownership and group broader tests by scenario.
- Mirror every production file and directory exactly in every test project.
- Keep test projects flat.

## Decision Outcome

Chosen option: "Mirror feature ownership and group broader tests by scenario",
because test paths should communicate intent without pretending that every
end-to-end or security scenario belongs to one production type.

Unit test classes belong under the narrowest matching production feature and
use namespaces matching their directories. Integration tests that exercise
several production types belong under descriptive `Scenarios` subdirectories.
Security tests belong under the production boundary they protect. Shared
fixtures, process harnesses, and test-only doubles belong under `TestSupport`;
they may retain the test project's root namespace when broad reuse makes that
clearer than repeated imports.

Merge duplicate tests or use arguments only when setup, action, and assertions
remain structurally identical with simple scalar or enum inputs. Keep separate
test methods when parameterization would require scenario switches, delegates,
or less precise diagnostics.

### Consequences

- Good, because test paths identify feature ownership or scenario intent.
- Good, because shared infrastructure no longer competes with test classes at
  project roots.
- Good, because conservative parameterization removes repetition without
  obscuring failures.
- Bad, because feature moves may require coordinated test moves and namespace
  updates.

### Confirmation

Review test paths and namespaces with production ownership. Confirm test project
roots contain only project metadata and intentional root-level CLI tests. Run
unit, integration, and security suites after structural changes.

## Pros and Cons of the Options

### Mirror feature ownership and group broader tests by scenario

- Good, because focused tests remain close to their production owner.
- Good, because cross-feature behavior keeps an honest scenario-level name.
- Bad, because placement requires judgment for tests spanning several features.

### Mirror every production file and directory exactly

- Good, because navigation is mechanically predictable.
- Bad, because integration and security scenarios often have no single matching
  production type.

### Keep test projects flat

- Good, because file moves and namespace updates are unnecessary.
- Bad, because ownership and scenario intent become implicit as suites grow.

## More Information

Extends [ADR-0062](0062-organize-cli-source-by-feature.md). See the
[C# coding guidelines](../../development/coding-guidelines/csharp.md).
