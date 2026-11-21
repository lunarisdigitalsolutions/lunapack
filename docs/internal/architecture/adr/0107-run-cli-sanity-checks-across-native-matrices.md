---
status: accepted
date: 2026-11-21
decision-makers:
  - Lunaris Engineering
---

# ADR-0107: Run CLI Sanity Checks Across Native Matrices

## Context and Problem Statement

ADR-0080 ran lifecycle sanity checks only for native x64 build entries. It
preserved an x64-only gap after macOS, Windows, and Linux build artifacts
passed compilation and unit tests. Arm64 native builds and PR-native matrices
could not exercise the complete Luna CLI lifecycle before release or merge.

## Decision Drivers

- Exercise every native RID publish output that the CLI release and PR matrices
  build.
- Run the same sanity script through the same workflow boundary on all supported
  hosts.
- Keep release-specific lifecycle validation out of the shared CLI build action.
- Surface macOS process-containment failures in PR-native jobs before merge.
- Avoid duplicate sanity implementations in release and pull-request workflows.

## Considered Options

- Keep sanity restricted to x64 entries.
- Add separate x64 and arm64 sanity jobs.
- Keep host-specific sanity steps duplicated across workflows.
- Add one reusable sanity action and call it from every native matrix entry.

## Decision Outcome

Chosen option: "Add one reusable sanity action and call it from every native
matrix entry," because all release and PR hosts can validate their local publish
directory with the same script and environment.

The reusable action accepts the native publish directory and source ref. Release
jobs call it for `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and
`osx-arm64`. The PR workflow calls it for Linux x64/Arm64, macOS x64/Arm64
when `ci-macos` is selected, and Windows x64 when `ci-windows` is selected.

The macOS process-containment fix is part of this matrix coverage. The security
test must keep a direct lifecycle process alive while it creates a detached
descendant, and the containment implementation must recheck and terminate known
macOS descendant process IDs and process groups.

### Consequences

- Good, because every native artifact executes the lifecycle sanity workflow
  before merge or release publication.
- Good, because release and PR workflows share one sanity environment.
- Good, because macOS detached-process termination is exercised on macOS PR
  runners.
- Bad, because native matrix jobs take longer and Arm64 runners consume additional
  CI capacity.

### Confirmation

The release workflow has no x64-only sanity condition. The PR workflow has
native Linux x64/Arm64, macOS x64/Arm64, and Windows x64 sanity steps. macOS
security tests cover detached-process termination, and the `ci-macos` PR label
reruns the macOS matrix.

## More Information

This decision supersedes
[ADR-0080](0080-sanity-check-x64-cli-artifacts-across-operating-systems.md)
and retains the native build, artifact, and NuGet relationships documented by
[ADR-0068](0068-publish-nuget-previews-from-main.md).
