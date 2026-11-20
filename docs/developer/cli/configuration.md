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
The lock document records the resolved commit used for each Git pack.

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

Use `luna config` to set, list, or remove Boolean preferences. Supported keys
are:

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

Preference values are optional in project and user settings. Project-local
values go in `lunapack.yml`:

```yml
preferences:
  discover:
    onlyComposites: true
  git:
    allowInsecureTransport: true
  nextSteps:
    enabled: false
```

`--project` stores values in the current `lunapack.yml`; `--local` is an alias.
`--user-project` stores values in the user profile's `.lunapack/config.yml`
under the canonical project directory. `--global` stores them in that file's
global settings. With no scope option, `luna config` uses `--global`. Scope
options are mutually exclusive. Writing or removing project values requires an
initialized workspace; `luna config list --project` reports an empty scope when
no `lunapack.yml` exists. `--workspace` selects which project receives
project-local or user-project values.

`luna config list` shows values explicitly stored in the selected scope; it
does not print built-in defaults. `set` accepts only `true` or `false`, and
unknown keys fail without changing settings. `remove` fails when the selected
scope has no value for that key.

For each key, Luna selects the first configured value from project-local,
user-project, and global settings, then uses the built-in default. An explicit
`false` overrides less-specific `true` values. `git.allow-insecure-transport`
applies to every `http://` and `git://` Git source in its selected scope; it is
not a per-source allowlist. Prefer HTTPS or SSH. See
[Git source behavior](../advanced/git-source-behavior.md#require-secure-transport)
for transport warnings, credential limits, and redirect behavior.

`--only-composites` enables the composite filter for one discover or search
invocation; it does not disable a filter already enabled by settings.
`--suppress-next-steps` always suppresses guidance for the current invocation.
