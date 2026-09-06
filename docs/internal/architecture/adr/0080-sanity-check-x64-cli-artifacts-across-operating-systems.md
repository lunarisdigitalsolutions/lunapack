---
status: accepted
date: 2026-09-02
decision-makers: LunaPack maintainers
---

# ADR-0080: Run x64 CLI Sanity Checks in Native Build Jobs

## Context and Problem Statement

The release workflow builds native CLI artifacts for Windows, Linux, and macOS,
but its pack lifecycle sanity check executed only the Linux x64 artifact. Build
and unit test success did not confirm that the packaged Windows and macOS x64
executables could run the same end-to-end CLI workflow before publication.

The native build runners already contain the complete source checkout and local
publish output. A separate sanity matrix would allocate duplicate runners and
repeat checkout and artifact transfer.

## Decision Drivers

- Exercise Windows, Linux, and macOS x64 publish output before release.
- Avoid duplicate runners, checkouts, and artifact transfers.
- Run sanity after the complete shared build action succeeds.
- Keep release-specific lifecycle validation out of the reusable build action.
- Continue excluding Arm64 from lifecycle sanity execution.

## Considered Options

- Keep the Linux x64 sanity check only.
- Add a separate sanity matrix for Windows, Linux, and macOS x64 artifacts.
- Add sanity execution to the shared CLI build composite action.
- Add an x64-gated sanity step after the build action in the release workflow.

## Decision Outcome

Chosen option: "Add an x64-gated sanity step after the build action in the
release workflow," because each native build runner can validate its local
publish output without broadening the shared build action's responsibilities.

The workflow build matrix runs `scripts/Test-CliSanity.ps1` after the `Build
Luna CLI` action when the runtime identifier ends in `-x64`. The script receives
the executable from the runner-local publish directory. Arm64 entries skip the
step. The release job depends on the aggregate build result, which includes all
three x64 sanity outcomes.

### Consequences

- Good, because x64 sanity checks reuse native build runners and checked-out
  source.
- Good, because sanity exercises publish output before the build job succeeds.
- Good, because the shared CLI build composite action remains reusable without
  release-specific lifecycle behavior.
- Bad, because each x64 build matrix entry takes longer to complete.
- Bad, because Arm64 artifacts still receive no lifecycle sanity execution.

### Confirmation

The CLI release workflow contract verifies that the sanity step follows the
build action, uses the local RID publish directory, runs only for x64 entries,
and does not appear in the shared CLI build composite action. It also verifies
that release depends on aggregate build success without a separate sanity job.

## More Information

The five-RID build and artifact-backed publication pipeline remains governed by
[ADR-0068](0068-publish-nuget-previews-from-main.md).
