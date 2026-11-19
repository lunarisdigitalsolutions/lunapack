---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0090: Constrain Lifecycle Hook Environment Warnings and Runtime

## Context and Problem Statement

Authorized lifecycle scripts inherit the CLI's entire process environment and
have no execution deadline. Users need visibility into ambient environment
variables and a bound on each hook's runtime, without mistaking policy for
script authorization or isolation.

## Decision Drivers

- Report environment exposure without logging variable values.
- Bound each script hook and terminate its process tree on timeout.
- Preserve existing child environment behavior in this rollout.
- Keep user policy compatible with older strict readers of `config.yml`.
- Keep lifecycle policy separate from pack-authored manifests and script trust.

## Considered Options

- Continue inheriting the full environment without warnings or timeouts.
- Filter the child environment using a configured allow-list.
- Warn about unlisted names, preserve inheritance, and add scoped timeouts.

## Decision Outcome

Chosen option: "Warn about unlisted names, preserve inheritance, and add scoped
timeouts", because it improves visibility and bounds execution while preserving
the current behavior of authorized hooks.

Project policy is optional under `trust.scripts` in `lunapack.yml`. Global-user
and canonical project-local user policy live in
`<profile>/.lunapack/lifecycle-hook-policy.yml`, separate from `config.yml`.
Policy does not grant script execution or override script denial.

The effective environment allow-list is the union of a built-in platform
baseline and additions from project, project-local user, and global-user
policy. LunaPack reports unlisted names only; hooks retain the complete
inherited environment. The effective timeout is the smallest explicitly
configured value from 1 through 1,800 seconds, or 300 seconds when no scope
specifies one. Lifecycle work runs under a supervised Windows job or Unix
process session. Root exit alone does not complete a hook while descendants
remain active. The deadline covers the supervised tree and captured-output
draining. Timeout terminates the process tree and fails lifecycle work; caller
cancellation remains a distinct outcome. A failed post-hook restores managed
files and exact original project-manifest bytes.

## Consequences

- Good, because warnings expose ambient variable names without logging values.
- Good, because each hook has a bounded runtime and timeout cleanup.
- Good, because the policy remains independent of script authorization and
  denial.
- Good, because the user sidecar leaves the legacy `config.yml` format
  unchanged.
- Good, because Windows user-policy project aliases normalize to the final
  physical key without changing ordinal identity comparison.
- Bad, because warnings do not prevent authorized scripts from reading or
  exfiltrating inherited values.
- Bad, because an explicit timeout may interrupt a hook that legitimately runs
  longer; the default is 300 seconds.
- Bad, because older CLI versions ignore both the project policy property and
  user sidecar and retain their prior unbounded hook behavior. Older strict
  external schema validators may reject `trust.scripts`.

## Confirmation

Project-schema and user-store tests validate policy boundaries, optional-field
compatibility, sidecar isolation, and canonical project scoping. Resolver tests
verify scope union, timeout precedence, defaults, and platform-specific name
comparison. Security process tests verify value-free warnings, inherited
values, timeout, cancellation, root-exit descendants, detached Linux
descendants, and output-pipe cleanup. Integration tests verify exact manifest
and managed-file rollback after timeout.

## More Information

- [Issue #74](https://github.com/lunarisdigitalsolutions/lunapack/issues/74)
- OpenSpec change: `constrain-lifecycle-hook-environment-and-runtime`
- [ADR-0087](0087-store-source-ignores-in-compatible-sidecar.md)
- [Lifecycle Hook Safety](../../development/lifecycle-script-safety.md)
