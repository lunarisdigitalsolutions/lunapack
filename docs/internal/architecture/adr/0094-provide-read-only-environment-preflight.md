---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0094: Provide A Read-Only Environment Preflight

## Context and Problem Statement

Missing Git, unavailable credential services, invalid Luna state, and
unwritable profile/cache directories previously surfaced as failures during
unrelated commands. Preference resolution also happened before command
dispatch, so it could prevent a diagnostic command from inspecting the
unreadable state that caused the problem.

## Decision Drivers

- Diagnose the selected project's configured environment before state changes.
- Keep normal invocations offline and preserve project, user, cache, and
  credential state.
- Avoid prompts, credential writes, executable hijacking, plaintext network
  access, redirects, or unbounded remote checks.
- Report unrelated environment problems without turning them into false
  blocking failures.
- Reuse existing source, state, credential, and process boundaries.

## Considered Options

- Add environment checks to every existing command.
- Add a fixed endpoint health check.
- Add a dedicated command that inspects local state and only probes configured
  remote sources after explicit opt-in.

## Decision Outcome

Chosen option: "Add a dedicated command that inspects local state and only
probes configured remote sources after explicit opt-in," because it keeps
diagnostics isolated from lifecycle operations and gives consumers an explicit
network boundary.

`luna doctor` runs through a lightweight command bootstrap that avoids
preference and full service-graph construction. It reads project state, user
settings, source-ignore state, Git availability, credential-backend support,
and relevant profile/cache directories. It never repairs files or creates
missing directories. Existing directories are probed with an unpredictable
create-new file, exclusive sharing, and delete-on-close; links and reparse
points are rejected.

The command reports `PASS`, `FAIL`, and `INFO`. Only a failure affecting an
operation configured for the selected project changes the exit code. The
network option checks non-ignored Git and OCI sources only, with a five-second
per-source limit and a 20-second overall limit. HTTP Git and `git://` transports
are not contacted. OCI redirects are disabled, and a locked pack selects its
configured repository for the minimal probe. HTTPS Git checks disable
credential helpers and prompts, ignore inherited system/global config and
environment-configured headers, and insert an option terminator before the
source URL. SSH checks use an absolute executable, a sanitized path, batch mode,
and existing SSH agent state without changing host configuration.

Native registry credentials are read only when the platform can do so without
interaction. The supported fast path is Windows Credential Manager. macOS and
Linux do not open potentially interactive keychain or Secret Service items
during doctor. A noninteractive ACR identity remains available through the
existing Azure provider. Doctor never prompts, saves, removes, or renders
credential values.

Git processes resolve bare executable names to an absolute file from absolute
`PATH` entries and skip the current workspace. This protects doctor and normal
Git operations from same-directory executable substitution. Doctor places an
option terminator before the configured Git URL and rejects option-like or
credential-bearing repository URLs before configuration can retain them.

### Consequences

- Good, because users can diagnose malformed state before normal command
  bootstrapping fails.
- Good, because the network and credential side effects are explicit, bounded,
  and independently testable.
- Good, because project, user, credential, and project-cache state remains
  unchanged after a successful doctor invocation.
- Good, because same-directory Git executable substitution is prevented across
  normal CLI Git workflows.
- Bad, because a private Git HTTPS source authenticated only by a credential
  helper is reported as not fully checked rather than definitively accessible.
- Bad, because macOS and Linux native OCI credentials cannot be confirmed
  without risking keyring interaction.
- Bad, because a fixed short timeout can classify a slow source as unavailable
  before a normal command with its configured timeout would fail.

### Confirmation

Unit and integration tests confirm early doctor bootstrap, project-state
preservation, project relevance, ignored/local source exclusion, bounded
timeouts, temporary-probe cleanup, output sanitization, Git executable
selection, no redirect following, and the absence of credential writes. Strict
OpenSpec validation confirms the public preflight contract.

## Pros and Cons of the Options

### Add checks to every existing command

- Good, because failures could appear closer to the operation.
- Bad, because every operation would gain network and diagnostic side effects.
- Bad, because unreadable preferences could still prevent the command from
  reaching the check.

### Probe a fixed public endpoint

- Good, because a simple connectivity test would be easy to implement.
- Bad, because connectivity to one host says little about configured project
  sources, credentials, DNS, or access policy.

### Add a dedicated, opt-in project preflight

- Good, because local diagnosis is always available and remote scope is
  explicit.
- Good, because project relevance controls the exit code.
- Bad, because consumers must invoke one additional command before another
  operation.

## More Information

This decision implements [issue #84](https://github.com/lunarisdigitalsolutions/lunapack/issues/84).
It complements [ADR-0019](0019-use-installed-git-for-pack-sources.md) and
[ADR-0085](0085-bind-registry-credentials-to-source-identities.md).
