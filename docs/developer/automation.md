# Use LunaPack in automation

Run Luna in CI with pinned inputs and explicit noninteractive policies. This
guide shows a repeatable validation and preview workflow for a pack from a Git
source.

## Prerequisites

- Install an exact Luna version from npm, NuGet, a release archive, or the
  container image.
- Make Git available when the workflow uses Git sources.
- Use a clean fixture workspace for installation tests. `luna init` refuses to
  overwrite existing project documents.

## Isolate user settings

Set `LUNAPACK_USER_PROFILE` to a job-owned directory. Luna stores user settings
beneath that directory's `.lunapack` folder instead of reading the runner's
normal profile.

```powershell
$env:LUNAPACK_USER_PROFILE = "$PWD/.ci-profile"
```

Do not cache this directory across untrusted jobs. It can contain persisted
source and pack trust decisions.

Committed project trust does not authorize hooks by itself. Project trust also
requires an acknowledgement in the selected user profile for the canonical
workspace path and exact source identity. A fresh job must review and establish
that acknowledgement with the matching `luna trust ... --project` command, use
another explicit script policy, or skip scripts. Reusing a profile across
checkout paths does not transfer project acknowledgement.

## Preview a pack

Initialize a fixture project, add a source pinned to a reviewed full commit ID,
then preview an exact pack release:

```powershell
$sourceCommit = 'REPLACE_WITH_FULL_COMMIT_SHA'
luna init
luna sources add github engineering `
  lunarisdigitalsolutions/lunapack `
  --ref $sourceCommit `
  --path projects/packs
luna install dotnet-project@1.0.0 `
  --dry-run `
  --skip-parameters `
  --scripts skip `
  --skip-instructions `
  --suppress-next-steps
```

Replace `REPLACE_WITH_FULL_COMMIT_SHA` with a commit available from your source,
and replace the pack release with a version available at that commit. The dry
run resolves and preflights changes without writing managed files or changing
LunaPack state. `--skip-parameters` suppresses prompts but still applies declared
defaults and explicit values; the preview fails if a required parameter remains
unresolved. Initialization and source registration do write `lunapack.yml` and
`lunapack-lock.yml` in the fixture.

When a pack declares additional Git sources, add `--accept-sources` only after
reviewing them. It approves conflict-free source additions; it does not bypass
Git authentication, path validation, lifecycle trust, or rollback.

Luna delegates source authentication to the installed Git client. Configure
credential helpers, SSH state, or other Git authentication for the job before
invoking Luna; Luna does not implement login or token refresh. GitHub shorthand
uses HTTPS. See [Understand Git source behavior](advanced/git-source-behavior.md)
for the complete boundary.

## Choose lifecycle behavior

Use `--scripts skip` when the job does not require lifecycle scripts. Use
`--scripts run` only after reviewing the exact source and hook arguments; hooks
run with the job's filesystem, process, network, and credential access.
`--scripts prompt` is the default and can require interactive consent, so do
not rely on it in unattended jobs.

Noninteractive instruction hooks print all prepared content without pausing.
Add `--skip-instructions` when the job must suppress that output or performs the
manual steps elsewhere. This option does not change script behavior.

## Handle results

Successful commands return exit code `0`. Invalid input, validation or
resolution failures, denied trust, and Git, filesystem, or state-write failures
return a nonzero code. Luna does not provide JSON output or stable diagnostic
codes, so automation should use process success for control flow and retain
human-readable logs for diagnosis.

Add `--workspace <directory>` when the project is not the process working
directory. Add `--suppress-next-steps` to omit contextual guidance from logs.
Use `--log-level debug` only for diagnosis because it produces more detailed
output.

The preview succeeds when Luna prints a complete plan and the process returns
`0`. Remove `--dry-run` only when the job is intended to apply managed files and
commit or publish the resulting workspace state.

See [Scripts and trust](cli/trust-and-scripts.md) for policy scopes and
[Command reference](cli/commands.md) for all options and failure behavior.
