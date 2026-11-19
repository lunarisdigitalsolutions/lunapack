# Design

## Context

`CliApplication.RunAsync` builds the command tree and resolves preferences before invoking most commands. That pre-command read can prevent diagnostics from running when project or user state is unreadable. The CLI already provides filesystem abstraction, project/user state readers, Git process execution, OCI authentication boundaries, and a platform-specific Git link cache root.

## Goals / Non-Goals

**Goals:**

- Keep environment checks independently testable and prevent command presentation from owning filesystem, process, or network policy.
- Preserve existing authentication and source-access behavior outside `luna doctor`.
- Ensure diagnostics remain available when preference resolution fails.

**Non-Goals:**

- Repair, initialize, or rewrite project/user state.
- Add credentials, change credential providers, or alter existing Git/OCI source behavior.
- Add general-purpose network diagnostics for arbitrary hosts.

## Decisions

### Isolate diagnostics behind a doctor feature

Add a doctor command handler and an environment-check service that returns structured check results. Each result carries status, whether it blocks configured project operations, a safe diagnostic, and remediation. The handler formats results through `CliConsole` and escapes source names, paths, and other untrusted text before rendering.

This keeps I/O policy testable and avoids turning the existing `audit` command, which reports installed pack/link state, into an environment-health command. No new package is needed.

### Bypass preference resolution for doctor

Recognize `doctor` alongside `config` in `ApplyPreferencesAsync`. It must not depend on successfully loading preferences before it can inspect broken state. The command still receives the resolved selected workspace for project-specific checks.

### Reuse existing state and path boundaries

Load project configuration and lock state through `ProjectStateStore`, user settings and source-ignore state through their existing stores, Git through `IGitProcessRunner`, and the cache path through `LinkSourceCacheRoot`. Use `IFileSystem` for inspection and probes. Do not add a second implementation of profile, cache, source identity, or state-file path rules.

Unavailable or invalid state is reported without creating replacement files. Missing project state is informational when no project operations are configured. Git or cache problems block only when the selected project configures operations that need them. Offline credential-store unavailability remains informational; an opt-in source check can establish whether the configured OCI source is actually inaccessible.

### Verify existing directory writability with an exclusive temporary probe

For an existing, relevant profile/cache directory, create a uniquely named file with create-new semantics, then remove only that file in a `finally` path. Never overwrite an existing path, create the target directory, or touch project state. Treat probe cleanup failure as a failed check and report it explicitly.

This tests actual write access rather than inferring it from platform-specific permission bits. A missing directory remains informational because normal operations create it when needed; doctor will not create it just to test its parent.

### Keep remote checks opt-in, source-scoped, and non-interactive

`--network` checks only configured Git and OCI sources selected by the workspace option. Git checks use a resolved absolute executable, argument-list invocation, a neutral working directory, closed stdin, an absolute SSH command, sanitized `PATH`, isolated system/global config, disabled credential-helper/askpass prompting, and an option terminator before the source URL. Plaintext HTTP and `git://` probes are skipped. OCI checks use the existing registry protocol with redirects disabled and source-bound credentials. A locked pack selects its exact configured repository; otherwise the registry endpoint is probed. They never prompt, save, remove, or render credentials. Windows Credential Manager can be read without interaction; macOS Keychain and Linux Secret Service are not opened because a lookup can require UI. Existing noninteractive ACR identity remains available.

Apply a five-second per-source timeout and a 20-second overall network-check budget. If the total budget expires, report remaining source checks as not checked and continue rendering local results. No remote request occurs without `--network`; local and ignored sources never cause network access.

An alternative was probing a fixed public endpoint. Configured-source probes better predict whether this project's actual remotes are reachable, while explicit opt-in limits the privacy and network side effects.

### Derive exit status from project relevance

Keep check status separate from blocker classification. A failed global prerequisite is blocking only when the selected project has a configured operation that depends on it. A missing/unreadable project manifest or a required source that fails an opted-in check is blocking; unrelated tools, absent optional directories, and checks not requested remain informational. Return non-zero only if at least one blocking failure exists.

## Risks / Trade-offs

- **A native credential-store read could trigger platform UI.** Use a non-interactive, read-only path. If a platform cannot be probed without prompting, report capability as informational instead of prompting.
- **Network checks disclose configured source access attempts to those hosts.** Keep them behind `--network`, identify each source before the probe, enforce per-source and total time bounds, and never retry indefinitely.
- **A process crash can interrupt temporary-file cleanup.** Use an unpredictable filename and exclusive creation; report normal cleanup failures and never remove a file that the check did not create.
- **Many configured sources can exceed the total budget.** Report every skipped source as not checked rather than implying success.

## Migration Plan

No persisted format or dependency changes. The command is additive; existing commands retain their current behavior. Rollback removes the doctor command and its documentation without migrating user or project state.
