---
status: accepted
decision-makers:
  - Lunaris Engineering
---

# ADR-0088: Share Changed-Pack Publishing Across Workflow Packs

## Context and Problem Statement

The generic OCI, ACR, and GHCR workflow packs all need to identify changed
direct-child folders under `packs/` and publish valid packs. Duplicated
discovery scripts can drift in path handling, deleted-pack behavior, and error
handling. Workflow packs should retain registry-specific authentication and
targets while sharing pack discovery and publication behavior.

## Decision Drivers

- Keep changed-pack discovery and publication logic in one maintained pack.
- Preserve per-registry workflow permissions and authentication.
- Handle whitespace, deleted packs, first pushes, and invalid paths safely.
- Keep registry artifact and identity logic in the Luna CLI.

## Considered Options

- Keep separate discovery scripts in each workflow pack.
- Duplicate discovery logic inline in each workflow.
- Compose one shared PowerShell publisher pack into all three workflows.

## Decision Outcome

Chosen option: "Compose one shared PowerShell publisher pack into all three
workflows," because it centralizes common path selection and publishing while
leaving registry configuration in each workflow.

The workflow packs SHALL use a SHA-pinned `tj-actions/changed-files` action to
produce JSON paths and pass those paths to the shared PowerShell publisher.
The publisher SHALL map changed files to direct-child pack folders, publish
each existing folder with `pack.yml` once, skip deleted or non-pack folders,
and fail on malformed paths or publishing errors. An all-zero before SHA SHALL
trigger a scan of all existing direct-child pack folders.

### Consequences

- Workflow packs depend on `github-publish-changed-packs` and install the
  shared script under `.github/scripts/`.
- The shared pack version and each dependent workflow pack reference must be
  updated together when the publishing contract changes.
- Publishing runners must provide PowerShell 7 (`pwsh`).
- Registry-specific credentials remain configured by each workflow and are
  never passed through LunaPack-specific OCI credential environment variables.

### Confirmation

Pack-validation integration tests verify the shared dependency and workflow
contracts. PowerShell fixture checks cover path selection, first-push scans,
invalid input, and publication failures. The `ci-pack-publishing` OpenSpec
specification records the workflow contract.

## Pros and Cons of the Options

### Separate discovery scripts per workflow pack

- Good, because each workflow is independently implemented.
- Bad, because behavior and security checks can drift between registries.

### Inline workflow discovery logic

- Good, because no helper pack is required.
- Bad, because scripts are duplicated in workflow definitions and harder to
  test consistently.

### Shared PowerShell publisher pack

- Good, because one versioned pack owns common discovery and publishing logic.
- Good, because each workflow keeps only target and authentication setup.
- Bad, because workflow packs must compose and version the shared dependency.

## More Information

See the [CI pack publishing specification](../../../../openspec/specs/ci-pack-publishing/spec.md)
and the [publishing workflow guide](../../../developer/advanced/publish-private-packs.md).
