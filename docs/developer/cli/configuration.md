# Configuration

Project state consists of portable configuration in `lunapack.yml` and generated
resolved state in `lunapack-lock.yml`. Both documents use `schemaVersion: 1` and
are created together by `luna init`.

Initialization writes only required properties: project `schemaVersion`,
`sources`, and `packs`, plus lock `schemaVersion` and `packs`. Later commands
add and canonically serialize properties required by their changes.

- `lunapack.yml`: Owned by the project team. It contains project-relative local
  sources, Git and OCI registry sources, requested root packs, project-owned
  links, and optional string and Boolean variables.
- `lunapack-lock.yml`: Owned by LunaPack. It records exact resolved packs and
  links, provenance, dependency edges, effective targets, and content SHA-256
  digests.

Variables can satisfy matching pack parameters but do not persist explicit
install input. Commands that change sources or roots validate and update both
documents as one recoverable state change.

During `luna install`, LunaPack prompts for each required parameter that is not
already supplied by an explicit parameter, a composite-pack binding, or a
matching project variable.

Local-source paths must be relative to the project. A Git source requires a
repository URL or absolute local filesystem repository path; it may select a
branch or commit and a repository-relative subdirectory. Git operations use a
configurable timeout from 1 through 300 seconds, with 300 seconds by default.
For Git repository identity checks, a trailing slash or `.git` suffix does not
make an otherwise equivalent URL a different source. A URL that identifies a
different repository is still rejected.

Git must be available on the process path. LunaPack caches discovered Git metadata
under the workspace `.lunapack` directory and never stores Git credentials there.
Online lifecycle operations resolve the configured ref or remote HEAD and reload
the manifest from that commit instead of trusting cached manifest content. The
cache can still support explicitly cached catalog presentation, but it does not
authorize installs, updates, scripts, or managed files. The lock document records
the resolved commit used for each Git pack.

OCI sources use an ordered `sources` sequence entry with `type: oci`, a required
HTTPS `url`, and an optional repository `path`. An optional `username` is
non-secret metadata; credentials are acquired separately and are never written
to project configuration. The URL identifies a registry origin, while the
repository prefix and pack ID identify an OCI repository. See
[Add a pack source](../sources.md#add-an-oci-registry-source) for normalization,
authentication, and discovery behavior.

Links select regular files from a configured local or Git source without
requiring an upstream `pack.yml`. Link intent remains in project configuration;
selected source paths, mapped targets, source identity, optional Git commit,
and content digests remain in lock state. See the [Luna Links reference](links.md).

## Trust storage

Project trust is stored in `lunapack.yml`. Local-user and global-user trust are
stored outside the repository in user settings. Packs cannot grant themselves
trust through `pack.yml`; consumers control every persisted trust entry.

A project source or pack declaration is effective only when user settings also
contain its acknowledgement for the canonical project path and exact source
identity. `luna trust ... --project` writes both records for the current user;
committed project trust alone does not authorize scripts for other users or
fresh automation profiles.

Source-ignore state is stored in the user-local
`<profile>/.lunapack/source-ignores.yml` sidecar and scoped to the canonical
project and source identity. Keeping it separate leaves `config.yml` unchanged
for older strict CLI readers. It is not shared through project configuration
or the lock file. Ignoring a source leaves existing trust and installed
ownership evidence intact.

All three scopes support `deny.scripts`. Any active denial overrides grants and
`--scripts run`. Project denial uses `trust.deny.scripts`; user settings use
`deny.scripts` in the corresponding project or global record. Omission and
explicit `false` mean no denial. `LUNAPACK_USER_PROFILE` can select an alternate
profile root for isolated automation; Luna stores settings beneath that root's
`.lunapack` directory.

Optional lifecycle script policy in `trust.scripts` controls environment
warnings and per-hook timeouts, not script authorization. Global-user and
project-local user policy use
`<profile>/.lunapack/lifecycle-hook-policy.yml`, leaving `config.yml` unchanged.
See [Lifecycle hooks and script trust](trust-and-scripts.md#lifecycle-script-policy)
for the policy fields, defaults, and precedence.

## CLI preferences

Use `luna config` to set, list, or remove Boolean preferences and resource
limits. Boolean preference keys are:

| Key                            | Built-in default | Effect                                           |
| ------------------------------ | ---------------- | ------------------------------------------------ |
| `discover.only-composites`     | `false`          | Show only composite packs in `luna discover`.    |
| `git.allow-insecure-transport` | `false`          | Allow `http://` and `git://` Git sources.        |
| `search.only-composites`       | `false`          | Show only composite packs in `luna search`.      |
| `next-steps.enabled`           | `true`           | Show contextual next-step and recovery guidance. |

```powershell
luna config set discover.only-composites true --project
luna config set next-steps.enabled false --user-project
luna config set git.allow-insecure-transport true --global
luna config set git.allow-insecure-transport false --project
luna config list --global
luna config remove discover.only-composites --project
```

Boolean preference values are optional in project and user settings.
Project-local values go in `lunapack.yml`:

```yml
preferences:
  discover:
    onlyComposites: true
  git:
    allowInsecureTransport: true
  nextSteps:
    enabled: false
  resourceLimits:
    packManifestBytes: 524288
```

### Resource limits

Resource-limit keys use positive decimal integers. Byte limits count raw bytes;
`MiB` and `GiB` below use binary units (`1 MiB = 1,048,576 bytes`,
`1 GiB = 1,073,741,824 bytes`). Project settings may lower defaults but cannot
raise them. Global and user-project settings may raise or lower defaults, up to
the finite user ceiling.

| Key                                      | Unit       | Default and project maximum | User ceiling |
| ---------------------------------------- | ---------- | --------------------------: | -----------: |
| `resource-limits.project-manifest-bytes` | bytes      |                       1 MiB |       16 MiB |
| `resource-limits.pack-manifest-bytes`    | bytes      |                       1 MiB |       64 MiB |
| `resource-limits.lock-file-bytes`        | bytes      |                      64 MiB |        1 GiB |
| `resource-limits.pack-document-bytes`    | bytes      |                      16 MiB |      256 MiB |
| `resource-limits.pack-snapshot-bytes`    | bytes      |                       2 GiB |       16 GiB |
| `resource-limits.rendered-file-bytes`    | bytes      |                      64 MiB |      256 MiB |
| `resource-limits.rendered-total-bytes`   | bytes      |                     512 MiB |        4 GiB |
| `resource-limits.dependency-depth`       | edges      |                          64 |          128 |
| `resource-limits.dependency-nodes`       | nodes      |                       1,024 |      100,000 |
| `resource-limits.dependency-references`  | references |                     100,000 |    1,000,000 |
| `resource-limits.files-per-pack`         | files      |                      10,000 |      100,000 |
| `resource-limits.files-per-graph`        | files      |                     100,000 |    1,000,000 |
| `resource-limits.oci-archive-entries`    | entries    |                     100,000 |    1,000,000 |
| `resource-limits.oci-download-bytes`     | bytes      |                     512 MiB |       16 GiB |
| `resource-limits.oci-entry-bytes`        | bytes      |                     512 MiB |        4 GiB |
| `resource-limits.oci-expanded-bytes`     | bytes      |                       2 GiB |       16 GiB |

`files-per-pack` and `files-per-graph` count both regular files and
directories. Files selected through Luna Links count toward the same snapshot
and graph budgets. Luna stops enumeration before retaining a collection above
those limits and streams selected link content under the aggregate snapshot
byte budget.

Luna also applies compiled safeguards that settings cannot raise: retained
pack-catalog manifest content and OCI repository/tag pagination payloads are
limited to 64 MiB in aggregate; Git path and external-source enumeration
output is limited to 64 MiB, and one path or metadata record is limited to
1 MiB. These safeguards bound intermediate process output and registry pages
even when the per-manifest, per-pack, or graph count limits are raised within
their user ceilings.

Before deserializing a lock file, Luna scans its YAML collections to reject an
over-limit pack list, reference list, link record, or managed-file list. YAML
aliases are rejected in lock state so one compact alias cannot expand retained
object counts after the preflight. Composite graph resolution shares one
catalog byte budget across every root and dependency lookup.

```powershell
luna config set resource-limits.pack-manifest-bytes 2097152 --global
luna config set resource-limits.files-per-pack 5000 --project
luna config list --global
luna config remove resource-limits.pack-manifest-bytes --global
```

`luna config set` rejects zero, negative, non-integer, overflowing, and
above-ceiling values. Zero never means unlimited. The compiled bootstrap
ceilings remain 16 MiB for `lunapack.yml` and 1 MiB for
`~/.lunapack/config.yml`; a settings file cannot raise the ceiling used to
read itself. Project-local resource limits are stored under
`preferences.resourceLimits` in `lunapack.yml`. User-owned limits are stored
under the matching scope's `preferences.resourceLimits` in
`~/.lunapack/config.yml`.

`--project` stores values in the current `lunapack.yml`; `--local` is an alias.
`--user-project` stores values in the user profile's `.lunapack/config.yml`
under the canonical project directory. `--global` stores them in that file's
global settings. With no scope option, `luna config` uses `--global`. Scope
options are mutually exclusive. Writing or removing project values requires an
initialized workspace; `luna config list --project` reports an empty scope when
no `lunapack.yml` exists. `--workspace` selects which project receives
project-local or user-project values.

`luna config list` shows values explicitly stored in the selected scope; it
does not print built-in defaults. `set` accepts `true` or `false` for Boolean
preferences and positive decimal integers for resource limits. Unknown keys
fail without changing settings. `remove` fails when the selected scope has no
value for that key.

The same effective limits apply when Luna publishes a pack to OCI. Publication
validates and archives the authoring tree and extracted result with the
project's resolved byte and entry limits before contacting the publication
endpoint. Pack-authoring manifest reads, writes, tree enumeration, and
validation also use the resolved values.

For Boolean preferences, Luna selects the first configured value from
project-local, user-project, and global settings, then uses the built-in
default. For resource limits, Luna selects the user-project value over global,
or the built-in default, and then applies the project-local value only as a
lower cap. A repository-controlled value therefore cannot raise a stricter
user setting. An explicit `false` overrides less-specific `true` values.
`git.allow-insecure-transport`
applies to every `http://` and `git://` Git source in its selected scope; it is
not a per-source allowlist. Prefer HTTPS or SSH. See
[Git source behavior](../advanced/git-source-behavior.md#require-secure-transport)
for transport warnings, credential limits, and redirect behavior.
`--only-composites` enables the composite filter for one discover or search
invocation; it does not disable a filter already enabled by settings.
`--suppress-next-steps` always suppresses guidance for the current invocation.
