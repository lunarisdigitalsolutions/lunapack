# Design

## Context

See `proposal.md` for motivation. Lifecycle scripts currently inherit the CLI process environment. Execution waits for process exit and then drains captured output; cancellation handling is separate from the new timeout. Script authorization and denial already have dedicated evaluation paths and must remain authoritative.

User trust settings in `config.yml` have no versioned migration contract. ADR-0087 keeps that file compatible with older strict CLI readers by placing new user-local state in a sidecar.

## Goals / Non-Goals

**Goals:**

- Add portable project policy and per-user global/project-local policy without changing script authorization.
- Keep user policy isolated from the legacy `config.yml` format.
- Make environment warnings value-free and ensure timeouts cover process completion and output draining.

**Non-Goals:**

- Remove, replace, or otherwise filter inherited environment variables in this rollout.
- Add lifecycle policy to `pack.yml` or add CLI commands for editing policy.
- Sandbox lifecycle scripts or claim that warning-only reporting prevents credential access.

## Decisions

### Store portable and user policy separately

Store portable policy in optional `trust.scripts` fields in `lunapack.yml`. Store global-user and canonical local-project user policy in `<profile>/.lunapack/lifecycle-hook-policy.yml`, with `global` and `projects` sections. Each policy record contains optional `allowedEnvironmentVariables` and `timeoutSeconds`. Project identities use the final physical path spelling, slash-only separators, and ordinal lookup; readers normalize legacy aliases for existing directories.

Use a dedicated sidecar rather than extending `config.yml`: older strict CLI readers may reject new properties, and that file has no versioned migration contract. The sidecar remains user-local, uses canonical project-directory keys, validates paths and values, and uses the existing user-settings file security and atomic-write protections. Do not add an acknowledgement to the existing script trust model; policy is not permission to execute a script.

### Resolve policy deterministically

The effective variable-name set is the union of a built-in platform baseline and additions from project, local-project user, and global-user policy. Compare names ordinally on case-sensitive platforms and ordinal-ignore-case on Windows. The baseline contains `PATH`, `TEMP`, `TMP`, and `TMPDIR` on all platforms; Windows additionally includes `PATHEXT`, `SYSTEMROOT`, `WINDIR`, `COMSPEC`, `USERPROFILE`, `HOMEDRIVE`, `HOMEPATH`, `APPDATA`, `LOCALAPPDATA`, and `SYSTEMDRIVE`; other platforms additionally include `HOME`, `SHELL`, `LANG`, `LC_ALL`, `LC_CTYPE`, and `TERM`.

The effective timeout is the minimum explicitly configured value, or 300 seconds when no scope specifies one. Validate values before execution: timeout is an integer from 1 through 1,800, and variable names are non-empty, unique within a scope, and contain neither `=` nor NUL. The operating system's name comparer also governs duplicate detection.

Keep policy resolution separate from source/pack authorization and script denial. Project policy can add names to the warning allow-list but cannot trust a pack, override denial, or change the child environment.

### Warn only after execution is authorized

Run environment comparison only for a script that passed condition, suppression, denial, and authorization checks and is about to launch. Report each unmatched variable name before launch; never format or log its value. Do not warn for instructions, denied or skipped scripts, false conditions, or dry runs.

Leave process environment construction unchanged so the child receives the same inherited environment as before. Use the allow-list only to identify names for warnings. Include an explicit public warning that this is advisory: an authorized script can still read or exfiltrate any inherited value.

### Bound each script process through cleanup

Apply the timeout to each script hook, not to the full lifecycle transaction. Pass the caller's cancellation token from the lifecycle service to the executor. Run each hook through an OS-contained supervisor so the execution remains tracked after the root process exits. Keep the deadline active through remaining descendants and completion of captured stdout/stderr reads; if it expires or cancellation wins, terminate the full process tree and await process/output cleanup. Do not tear down descendants after an ordinary completion with no active hook process.

Represent timeout and user cancellation as distinct execution outcomes rather than inferring cancellation from error text. Map timeout to hook failure and existing failure/rollback behavior. Preserve user cancellation as cancellation. If caller cancellation and timeout race, caller cancellation takes precedence. Surface process-tree cleanup failures rather than reporting a successful timeout cleanup.

### Preserve version-1 and pack boundaries

Extend the `lunapack.yml` schema with optional `trust.scripts` settings; do not require migration for existing version-1 documents. Keep `pack.yml` unchanged. The user sidecar is validated by its runtime settings contract and leaves `config.yml` byte-for-byte untouched when policy is saved. `ProjectStateStore` ignores unmatched YAML properties, so older CLI versions can still read `lunapack.yml` but ignore `trust.scripts` and do not enforce its warnings or timeout. External validators using a strict schema may reject the new property; document both downgrade behaviors.

Record the accepted sidecar, trust-boundary, and lifecycle-timeout decisions in a new ADR, using the next available index after checking `docs/internal/architecture/adr/index.md`.

## Risks / Trade-offs

- **Warning-only mode still exposes inherited values to authorized scripts.** Keep warnings value-free, state clearly that they are not isolation, and defer filtering to a separately reviewed change.
- **Portable project policy can suppress warnings for listed names.** Keep script authorization and denial independent, and call out the trust implications in public guidance and manual review.
- **Several hooks can each consume their full timeout.** Document that the limit is per script hook; users can configure a shorter per-hook limit.
- **A timeout may race with process exit or output draining.** Keep the deadline active through all completion tasks and test both descendants and output-pipe cleanup.
- **A malformed or redirected user sidecar could alter policy scope.** Reuse canonical-path validation, path-security checks, explicit validation errors, and atomic writes.

## Migration Plan

Existing project manifests and `config.yml` files need no migration. Missing project policy and a missing user sidecar use the built-in warning list and 300-second timeout. Saving user policy creates or updates only `lifecycle-hook-policy.yml`. Removing that sidecar restores user defaults. Older CLI versions ignore both the project policy property and user sidecar, so they retain their prior unbounded hook behavior; use a version supporting this policy whenever its warning and timeout guarantees are required. Strict external schema validators may require removing `trust.scripts` before validating with an older schema.

## Open Questions

None.
