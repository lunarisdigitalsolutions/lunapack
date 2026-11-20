# Proposal

## Why

Approved lifecycle scripts currently inherit the CLI's entire process environment and have no execution deadline. This exposes unrelated credentials to hooks and lets a stalled process block a terminal or automation runner.

## What Changes

- Add optional lifecycle-script policy to portable project configuration, project-local user settings, and global user settings.
- Extend a platform-aware built-in environment allow-list with names configured in any scope. Initially report inherited variable names outside that list without logging values or changing the child environment.
- Apply a scoped script timeout: 300 seconds by default, configurable from 1 through 1,800 seconds, with the smallest explicitly configured value taking effect.
- On timeout, terminate the full process tree and report timeout separately from user cancellation.
- Preserve compatibility with existing version-1 project and user settings. Do not add policy fields to `pack.yml` or enforce environment filtering in this rollout.
- Update lifecycle safety specifications, public and maintainer documentation, and the CLI changelog.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-pack-lifecycle`: warn about environment variables outside the effective allow-list and enforce bounded script execution.
- `cli-project-configuration`: persist and combine lifecycle-script policy across project, project-local user, and global user scopes.
- `manifest-schemas`: describe optional policy fields while preserving existing version-1 documents.

## Impact

The CLI lifecycle executor and trust-policy evaluation; project and user settings models, stores, validators, and YAML serialization; the published project schema; lifecycle tests; OpenSpec specifications; public and internal documentation; and the CLI changelog. No new runtime dependency is planned.
