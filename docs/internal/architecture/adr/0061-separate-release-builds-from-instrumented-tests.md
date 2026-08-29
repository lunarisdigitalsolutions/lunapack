---
status: accepted
date: 2026-08-29
decision-makers: LunaPack maintainers
---

# Separate release builds from instrumented tests

## Context and Problem Statement

LunaPack Release builds intentionally disable debug symbols and strip symbols
for Native AOT distribution. Microsoft Code Coverage requires instrumentable
managed binaries, so collecting coverage from those Release outputs produced
empty Cobertura reports that appeared to have 100% line and branch coverage.

The CLI pipeline must preserve strict Release validation while producing
reproducible, source-only coverage for each test project. Maintainers also need
line and branch results in the GitHub Actions summary without depending on an
external coverage service.

## Decision Drivers

- Keep Release build, analyzer, formatter, trimming, and publication behavior
  unchanged.
- Reject empty or generated-code-dominated reports.
- Preserve separate unit, integration, and security coverage evidence.
- Use the repository's Microsoft Testing Platform coverage extension.
- Keep coverage available without external accounts or secrets.

## Considered Options

- Enable symbols in every Release build.
- Collect coverage from a separate Debug test build.
- Replace the existing collector with an external coverage service.

## Decision Outcome

Chosen option: "Collect coverage from a separate Debug test build", because it
keeps distribution validation independent from instrumentation requirements.

The pipeline restores and builds the complete solution in Release, then runs
each test project in Debug with Microsoft Code Coverage. A checked-in settings
file includes only the production CLI module and excludes generated sources.
Each test project writes deterministic TRX and Cobertura files. The pipeline
publishes line and branch rates to the job summary and fails when a successful
test run creates an empty coverage report.

### Consequences

- Good, because Release and Native AOT behavior remain unchanged.
- Good, because unit, integration, and security reports remain attributable.
- Good, because generated code no longer distorts coverage rates.
- Good, because empty reports cannot present false 100% metrics.
- Bad, because CI builds testable Debug binaries in addition to Release output.
- Bad, because the summary reports suites independently rather than presenting
  merged execution coverage.

### Confirmation

Run the coverage commands in the
[testing strategy](../../development/testing-strategy.md). Confirm each report
contains nonzero `lines-valid` and `branches-valid` attributes and no source
path under `obj`. In GitHub Actions, confirm the CLI job summary contains one
line and branch row for every test project.

## Pros and Cons of the Options

### Enable symbols in every Release build

- Good, because tests could reuse Release outputs.
- Bad, because instrumentation concerns would alter distribution build policy
  and runtime-specific publication inputs.

### Collect coverage from a separate Debug test build

- Good, because coverage and release concerns remain independent.
- Good, because the existing collector supports deterministic reports and
  source filters.
- Bad, because CI performs additional compilation.

### Replace the existing collector with an external coverage service

- Good, because a service could merge reports and host a public badge.
- Bad, because it adds governance, credentials, availability, and dependency
  requirements that are unnecessary for build-summary reporting.

## More Information

- [ADR-0006](0006-establish-dotnet-quality-baseline.md)
- [Testing strategy](../../development/testing-strategy.md)
