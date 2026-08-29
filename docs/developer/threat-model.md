# Security model and risks

LunaPack manages files from local or Git sources and can run pack-provided
lifecycle scripts. This page explains what LunaPack validates, which decisions
remain yours, and which risks LunaPack does not remove.

LunaPack is not a sandbox or privilege boundary. Treat every pack and source as
code from its publisher.

## Trust boundaries

Security decisions cross these public boundaries:

- Your project trusts selected local directories or Git repositories as pack
  sources.
- Luna reads project configuration, generated lock state, pack manifests,
  templates, and selected source files.
- Managed-file operations write within the selected workspace.
- Approved lifecycle scripts run as your current user.
- Installation packages and container images come from external distribution
  services.

LunaPack assumes your operating system, Git client, container runtime, and
selected distribution service enforce their own security boundaries.

## Sources and pack identity

Pack IDs identify content within configured sources; they do not prove publisher
identity. Luna normalizes source identities and records exact Git commits in
`lunapack-lock.yml`, which supports reproducibility and drift detection. This
provenance is not a cryptographic signature.

Review a source URL, ref, path, and owner before adding it. Use a full commit ID
when a workflow must not accept upstream movement. Tags can move unless the Git
host enforces protection; branch refs intentionally allow later updates to
consume changed content from the same source.

Pack-defined external Git sources require separate consumer approval. Dry-run
output shows proposed mappings and additions. `--accept-sources` approves only
conflict-free additions; it does not bypass authentication, path checks, script
trust, or rollback.

## Lifecycle scripts

An approved script can read available credentials, modify any file your user
can access, use the network, and start other processes. LunaPack cannot reverse
remote calls or unrelated filesystem changes made by a script.

Lifecycle commands use `--scripts prompt|run|skip`:

- `prompt` uses persisted trust or asks before each untrusted script. Enter
  declines.
- `run` permits non-suppressed scripts for that invocation.
- `skip` runs no scripts.

Persistent script denial overrides every grant and command mode. Grant the
narrowest trust scope only after reviewing exact source content and rendered
arguments. In automation, prefer `--scripts skip` unless scripts are required
and reviewed.

Instruction hooks display Markdown and never launch a process. They can still
contain misleading instructions or links. Review them before acting; use
`--skip-instructions` when automation handles those steps elsewhere.

See [Scripts and trust](cli/trust-and-scripts.md) for scope and denial behavior.

## Managed files and paths

Project, source, selector, target, destination, and remapping paths must stay
within their defined roots. Luna rejects rooted or escaping project-relative
paths. It records declared and effective targets plus content digests so update,
audit, move, and uninstall operations can distinguish owned content from local
changes.

Content digests do not make local edits immutable. During update,
`copy:overwrite` replaces current content, `backup-and-overwrite` creates a
backup before replacement, `skip-if-exists` retains the target, and
`fail-if-exists` stops. Merge strategies combine pack content with the current
target according to their method. Preview updates and choose pack strategies
that match the target's ownership expectations. Uninstall uses recorded
ownership and strategy state to avoid deleting unrelated content.

Transactions can restore LunaPack-managed files and state after handled
failures, but they cannot reverse script side effects or changes made by another
process at the same time.

LunaPack rejects symbolic links and reparse points selected by Luna Links. Pack
source snapshotting does not currently guarantee race-free, no-follow traversal
against another process running as the same user. Do not consume a source that
an untrusted local process can modify during an operation.

## Templates and resource use

Template rendering is opt-in through `template: true` or instruction
`templating: true`. Templates receive resolved pack values and supported
functions, not direct filesystem or host-service access.

Complex templates, very large files, and unusually broad dependency graphs can
consume substantial CPU, memory, or disk. Current public schemas do not define
complete resource ceilings. Inspect unfamiliar packs and use an isolated
workspace before applying them to important projects.

## Credentials and diagnostics

Do not place credentials in source URLs, manifests, command arguments, or
committed project state. Luna does not store Git credentials in its workspace
cache, but Git and lifecycle processes can use credentials available in their
environment.

Diagnostics and hook output can contain private paths, repository details, or
tool output. Redact credentials, private URLs, usernames, and project data
before sharing logs. LunaPack does not collect telemetry.

## Distributions and containers

Pin an exact Luna package or image version in reproducible automation. Verify
downloaded release archives with their `SHA256SUMS.txt` file. A checksum detects
changed bytes after publication; it does not independently prove publisher
identity.

The container needs a writable project mount to manage files. Mount only the
intended workspace, avoid privileged runtime options, and run with the least
host access required. Mutable image tags such as `latest` can identify different
bytes over time.

## Safer operating practice

1. Add only sources whose identity and ownership you have reviewed.
2. Inspect an exact pack release before installation.
3. Run installation and update with `--dry-run` before applying changes.
4. Review managed targets, external source additions, templates, and lifecycle
   arguments.
5. Skip scripts or use the narrowest suitable trust scope.
6. Run untrusted evaluations in a disposable workspace with minimal credentials.
7. Keep `lunapack-lock.yml` under review and use `luna audit` to investigate
   ownership, provenance, or drift.

Report suspected vulnerabilities through the
[Security Policy](https://github.com/lunarisdigitalsolutions/lunapack/blob/main/SECURITY.md).
