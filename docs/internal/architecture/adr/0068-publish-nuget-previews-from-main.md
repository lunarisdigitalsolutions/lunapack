---
status: accepted
date: 2026-08-31
decision-makers: LunaPack maintainers
---

# ADR-0068: Unify the CLI Release Artifact Pipeline

## Context and Problem Statement

Stable and preview CLI releases used separate job graphs. Stable builds created
native artifacts and published centrally, while preview runners built and
published RID packages independently before a separate pointer-package job.
This duplicated target configuration, release authentication, and ordering
logic in the workflow.

## Decision Drivers

- Keep the five native builds on matching runners.
- Publish every RID package before the NuGet pointer package.
- Authenticate and publish from one release job.
- Keep stable and preview channel selection explicit.
- Remove duplicated workflow matrices and preview publication jobs.

## Considered Options

- Keep separate stable and preview job graphs.
- Put the pointer package in the native build matrix.
- Use one artifact-backed plan, build, and release pipeline.

## Decision Outcome

Chosen option: "Use one artifact-backed plan, build, and release pipeline,"
because the build matrix remains the native execution boundary while its
completion provides the barrier required before central publication.

The plan job resolves either a stable tag version or a MinVer preview version
and emits one target matrix. Every matrix runner builds, tests, packs, and
uploads its RID package. One release job downloads and validates the complete
RID package set, creates the pointer package, authenticates once, and publishes
the five RID packages before the pointer package. Stable releases additionally
prepare and publish the GitHub, container, and npm channels; preview releases
select only NuGet.

### Consequences

- Good, because stable and preview releases use the same three-job graph.
- Good, because pointer publication cannot start before all native builds pass.
- Good, because NuGet authentication and publication order have one owner.
- Good, because every preview target receives the same build and test checks as
  a stable target.
- Bad, because preview runs create short-lived archives and NuGet workflow
  artifacts that only NuGet packages consume.
- Bad, because the shared release job receives the stable permission superset
  even when preview channel selection invokes only NuGet.

### Confirmation

The distribution contract test verifies shared plan outputs, native build
matrix use, artifact upload and download, one release action invocation, one
federated NuGet login, exact RID package validation, preview-only NuGet channel
selection, and RID-before-pointer publication order.

## More Information

This decision supersedes
[ADR-0067](0067-own-and-package-the-cli-changelog.md). Channel-owned publishers
and preview changelog behavior remain unchanged.
