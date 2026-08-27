# Troubleshooting

Start with the smallest failing command and add `--log-level debug`. Redact
credentials, repository URLs that are private, local usernames, and project data
before sharing output.

## Project state already exists

`luna init` refuses to overwrite either project document. Use the existing
`lunapack.yml` and `lunapack-lock.yml`, or remove both only when intentionally
starting unmanaged state again.

## Pack not found or version unavailable

Run `luna sources list`, then `luna discover --versions 10`. Confirm source
paths and Git refs, and use `luna validate <id>@<version>` for a known candidate.
An omitted version selects the highest Semantic Version across configured
sources; equal versions prefer the earliest configured source.

## Git source fails or times out

Confirm `git` is on the process path and the repository can be fetched with the
same user credentials outside Luna. Git source `timeoutSeconds` accepts 1 through
300 and defaults to 300. Luna does not store Git credentials in its workspace
cache.

## Install reports a target conflict

Use `--dry-run` to identify the owner and planned action. Adopt an identical
existing file with `--adopt-existing`, choose a safe destination or remapping,
or resolve conflicting pack versions. Do not delete a consumer-owned file only
to make installation pass.

## A managed file was changed

Update and uninstall preserve content whose current digest differs from the
recorded installed digest. Review and reconcile it manually. `luna audit` shows
current ownership; `luna mv` relocates one uniquely owned managed file.

## A lifecycle script is denied

Review the source and hook first. Use `--scripts skip` when files can be applied
without automation, grant the narrowest appropriate trust scope, or use
`--scripts run` for one reviewed invocation. See
[Scripts and trust](cli/trust-and-scripts.md).

## Lifecycle instructions are unsuitable for automation

Noninteractive sessions print every prepared instruction without waiting for
input. Use `--skip-instructions` when automation must suppress that output or
when manual setup is handled elsewhere. This option does not change script
consent; combine it with the intended `--scripts` mode explicitly.

## Legacy lock migration fails

Luna cannot migrate a version-1 managed-file record when its declared target
cannot be derived safely. Restore a lock file produced by the installing Luna
version or rebuild project state in a clean test copy. Do not hand-edit ownership
or digests without verifying every managed file.

## Reporting a defect

Follow the repository support policy. Include Luna version, operating system,
installation method, exact command, a minimal synthetic pack, expected and
actual behavior, exit code, and redacted diagnostics.
