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

Luna delegates authentication to Git and inherits the invoking environment;
GitHub shorthand uses HTTPS. Test with the same transport, operating-system
user, credential helper or SSH agent, and environment as the Luna process.

For an unpinned source, Luna remembers the previously resolved default branch.
If a host changes its default while the old branch remains, configure an
explicit `ref` or remove that source's JSON entry below
`.lunapack/git-sources` to force new remote-HEAD discovery. A terminated process
can leave workspaces below the system temporary `lunapack` directory; remove a
stale workspace only after confirming no Luna process uses it. See
[Understand Git source behavior](advanced/git-source-behavior.md).

## Adding or removing a source fails

Luna canonicalizes a source's repository, ref, and path before comparing it to
configured sources, so registering the same repository under a different URL
form, casing, or name fails with a "duplicates source" error; run
`luna sources list` to find the name already bound to that identity, or
`luna sources rename` it. `luna sources rm` refuses removal while
`lunapack-lock.yml` records an installed pack or its external content as a
consumer; run `luna audit` to find those packs, then uninstall or reinstall
them from another source before removing it.

## External source approval or drift fails

Run the install or update with `--dry-run` to inspect pack aliases, authoritative
workspace mappings, proposed additions, and file actions. If a proposed name is
already used by a different fingerprint, add the required source explicitly
under another name before retrying. Update blocks when a configured repository,
canonical ref, or base path differs from locked provenance; inspect both
sanitized fingerprints with `luna audit` instead of editing the lock file.

## Install reports a target conflict

Use `--dry-run` to identify the owner and planned action. Adopt an identical
existing file with `--adopt-existing`, choose a safe destination or remapping,
or resolve conflicting pack versions. Do not delete a consumer-owned file only
to make installation pass.

## A managed file was changed

Run `luna audit` first. Local drift alone does not trigger an update when the
newly rendered pack bytes still match the locked digest. When desired pack
content changes, the configured strategy decides the action. When a new pack
version removes the target entirely, update can delete the file without a drift
check; preview the update and use `@ignore` when ownership should be dropped but
the file retained. Explicit uninstall rejects deletion of modified owned
content. `luna mv` relocates a managed file or directory and can save the
relocation as a reusable mapping with `--save-remap`.

## A lifecycle script is denied

Run `luna trust list` for local-user policy, or add `--project` or `--global` to
inspect another scope. A `policy-denied` hook cannot be enabled with
`--scripts run` or a positive grant. Reset every reported denial scope only
after reviewing retained grants; reset requires interactive confirmation.
Without persistent denial, use `--scripts skip`, grant the narrowest appropriate
trust scope, or use `--scripts run` for one reviewed invocation. See
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
