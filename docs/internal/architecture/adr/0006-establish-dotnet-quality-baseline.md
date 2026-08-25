---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0006: Establish the .NET Quality Baseline

## Context and Problem Statement

The first CLI requires repeatable formatting, analyzer, and testing practices.

## Decision Drivers

- Enforce repeatable formatting and static analysis for the CLI.
- Fail builds when quality warnings are introduced.
- Test isolated behavior and real command-process behavior separately.

## Considered Options

- Use .NET 10, CSharpier, analyzers, and separate TUnit test projects.
- Enforce formatting only.
- Use unit tests only.

## Decision Outcome

Chosen option: "Use .NET 10, CSharpier, analyzers, and separate TUnit test
projects", because it combines formatting, maintainability checks, isolated
tests, and real CLI-process coverage.

### Consequences

- Good, because every warning blocks builds and formatting is enforced.
- Good, because integration tests run the built CLI while unit tests exercise isolated command and filesystem paths.
- Bad, because contributors must maintain separate unit and integration suites.

### Confirmation

The .NET build enforces analyzers and formatting; the unit and integration test
projects run independently in the repository quality checks.

## Pros and Cons of the Options

### Use .NET 10, CSharpier, analyzers, and separate TUnit test projects

- Good, because it covers formatting, analysis, and both testing levels.
- Bad, because the quality baseline has several maintained tools and projects.

### Enforce formatting only

- Good, because it offers a small, fast initial quality gate.
- Bad, because it does not catch correctness and maintainability issues.

### Use unit tests only

- Good, because tests can stay fast and isolated.
- Bad, because they do not verify process and working-directory behavior.

## More Information

Use .NET 10, CSharpier, .NET analyzers, Meziantou.Analyzer, and TUnit. Keep
unit and integration tests as separate projects under `projects/cli/src/`.
