# Configuration

Project state consists of portable configuration in `lunapack.yml` and generated
resolved state in `lunapack-lock.yml`. Both documents use `schemaVersion: 1` and
are created together by `luna init`.

Initialization writes only required properties: project `schemaVersion`,
`sources`, and `packs`, plus lock `schemaVersion` and `packs`. Later commands
add and canonically serialize properties required by their changes.

- `lunapack.yml`: Owned by the project team. It contains project-relative local
  and Git sources, requested root packs, project-owned links, and optional
  string and Boolean variables.
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

Git must be available on the process path. LunaPack caches discovered Git metadata
under the workspace `.lunapack` directory and never stores Git credentials there.
The lock document records the resolved commit used for each Git pack.

Links select regular files from an existing configured source without requiring
an upstream `pack.yml`. Link intent remains in project configuration; selected
source paths, mapped targets, source identity, optional Git commit, and content
digests remain in lock state. See the [Luna Links reference](links.md).

## Trust storage

Project trust is stored in `lunapack.yml`. Local-user and global-user trust are
stored outside the repository in user settings. Packs cannot grant themselves
trust through `pack.yml`; consumers control every persisted trust entry.

All three scopes support `deny.scripts`. Any active denial overrides grants and
`--scripts run`. Project denial uses `trust.deny.scripts`; user settings use
`deny.scripts` in the corresponding project or global record. Omission and
explicit `false` mean no denial. `LUNAPACK_USER_PROFILE` can select an alternate
profile root for isolated automation; Luna stores settings beneath that root's
`.lunapack` directory.
