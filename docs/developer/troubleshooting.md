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

## Adding or removing a source fails

Luna canonicalizes a source's repository, ref, and path before comparing it to
configured sources, so registering the same repository under a different URL
form, casing, or name fails with a "duplicates source" error; run
`luna sources list` to find the name already bound to that identity, or
`luna sources rename` it. `luna sources rm` refuses removal while
`lunapack-lock.yml` records an installed pack or its external content as a
consumer; run `luna audit` to find those packs, then uninstall or reinstall
them from another source before removing it.

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

## Legacy lock migration fails

Luna cannot migrate a version-1 managed-file record when its declared target
cannot be derived safely. Restore a lock file produced by the installing Luna
version or rebuild project state in a clean test copy. Do not hand-edit ownership
or digests without verifying every managed file.

## Reporting a defect

Follow the repository support policy. Include Luna version, operating system,
installation method, exact command, a minimal synthetic pack, expected and
actual behavior, exit code, and redacted diagnostics.
