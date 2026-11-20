# Proposal

## Why

Missing tools or credentials, unreadable state, and unwritable profile/cache directories currently surface as failures in later commands with little context. A dedicated preflight command can identify project-relevant blockers early and provide actionable remediation without changing project state.

## What Changes

- Add `luna doctor` with pass, fail, and informational results for Git, credential storage, project/user state files, and profile/cache directories.
- Keep checks offline by default. Add opt-in, time-bounded checks for configured Git and OCI sources in the selected project.
- Return non-zero only when a failure prevents an operation configured for the selected project.
- Allow temporary create/delete probes in existing profile/cache directories; never modify project state, prompt for credentials, save credentials, or display secret values.
- Let `luna doctor` run even when preference or project state is unreadable, so it can report the problem.

## Capabilities

### New Capabilities

- `cli-environment-preflight`: diagnose the current environment and selected project's configured operations without changing project state.

### Modified Capabilities

No existing capability requirements change. The new command observes existing project, Git, OCI, and credential behavior without changing those contracts.

## Impact

- CLI command registration and pre-command preference resolution.
- Project and user state readers, Git process runner, native credential store, and profile/cache path resolution.
- Developer command/environment guidance, CLI changelog, and internal architecture decision record.
- Unit and CLI integration tests for diagnostics, exit behavior, side-effect boundaries, and bounded network checks.
