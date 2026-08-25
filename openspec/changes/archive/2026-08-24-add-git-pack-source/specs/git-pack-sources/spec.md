## Purpose

Enable consumers to discover and install versioned engineering packs from a remote Git repository with reproducible, commit-pinned source evidence.

## ADDED Requirements

### Requirement: Configure a Git pack source

LunaPack SHALL support a `git` entry in `lunapack.yml` with a repository URL, optional `ref`, optional repository-relative `path`, and optional `timeoutSeconds`. `ref` SHALL identify either a branch or commit SHA. `path` SHALL constrain discovery to that repository subtree. `timeoutSeconds` SHALL be an integer from 1 through 300 and SHALL default to 300 when omitted. LunaPack SHALL reject invalid Git source configuration before catalog or lifecycle operations mutate project state.

#### Scenario: Configure a branch-scoped Git source

- **WHEN** `lunapack.yml` contains a Git source with a repository URL, `ref`, and repository-relative `path`
- **THEN** LunaPack uses that configured source to locate packs only at the requested ref and path

#### Scenario: Reject an excessive Git timeout

- **WHEN** `lunapack.yml` contains a Git source whose `timeoutSeconds` exceeds 300
- **THEN** LunaPack rejects the configuration and does not run a Git operation

### Requirement: Resolve a Git source to an immutable commit

For every Git source catalog operation, LunaPack SHALL resolve the requested ref to a commit SHA before reading source content. When `ref` is absent, LunaPack SHALL determine the repository default branch from remote `HEAD` and resolve its current commit. The resulting commit SHA SHALL be used consistently for that operation.

#### Scenario: Resolve an omitted ref from remote HEAD

- **WHEN** a configured Git source omits `ref`
- **THEN** LunaPack resolves the remote default branch and catalogs the commit currently referenced by that branch

#### Scenario: Resolve a commit-pinned source

- **WHEN** a configured Git source specifies a commit SHA as `ref`
- **THEN** LunaPack catalogs content from that exact commit

### Requirement: Discover Git-source pack manifests efficiently

LunaPack SHALL recursively discover `pack.yml` files at the resolved commit, limited to the configured repository path when present and to the repository root otherwise. It SHALL read each discovered manifest and include only schema-valid packs in the catalog. It SHALL not transfer Git history during discovery or materialization.

#### Scenario: Discover manifests under a configured subtree

- **WHEN** a Git source specifies `path: packs/platform` and the resolved commit contains manifests both inside and outside that subtree
- **THEN** LunaPack catalogs only valid manifests inside `packs/platform`

#### Scenario: Continue after an invalid Git-source manifest

- **WHEN** a discovered Git-source `pack.yml` is invalid and another discovered manifest is valid
- **THEN** LunaPack excludes the invalid candidate and retains the valid candidate in the catalog

### Requirement: Cache resolved Git-source catalog metadata

LunaPack SHALL persist Git-source catalog metadata under the project `.lunapack` directory, including the source identity, resolved commit, default branch when one was determined, and discovered pack identities, versions, and repository-relative paths. On a later catalog operation, LunaPack SHALL compare the current resolved commit with cached metadata and reuse the cached catalog when they match.

#### Scenario: Reuse unchanged Git catalog metadata

- **WHEN** a Git source resolves to the same commit recorded in its cache
- **THEN** LunaPack uses the cached pack metadata without rediscovering repository files

#### Scenario: Refresh changed Git catalog metadata

- **WHEN** a Git source resolves to a commit different from its cached commit
- **THEN** LunaPack refreshes the discovered pack metadata and replaces the cache entry

### Requirement: Use an installed Git client safely

LunaPack SHALL execute Git operations through the locally installed `git` executable with timeout and cancellation enforcement. It SHALL pass repository URLs, refs, paths, and other Git values as discrete process arguments without invoking a command shell. LunaPack SHALL return a non-success result when Git is unavailable, times out, or reports an error.

#### Scenario: Reject a timed-out Git operation

- **WHEN** a Git operation does not finish within its configured timeout
- **THEN** LunaPack terminates the operation, reports a non-success result, and does not write partial cache or lifecycle state

#### Scenario: Treat source values as process arguments

- **WHEN** a Git source URL, ref, or path contains shell-special characters
- **THEN** LunaPack passes the value to Git without shell interpretation

### Requirement: Materialize selected Git packs at their resolved commit

When installing or updating a pack resolved from a Git source, LunaPack SHALL obtain the selected pack directory and all content referenced by its manifest from the resolved commit without transferring repository history or unrelated pack directories. Composite pack references resolved from the same or another Git source SHALL follow normal source precedence and each selected pack SHALL be materialized from its own resolved commit.

#### Scenario: Install a Git-sourced pack without unrelated pack content

- **WHEN** a selected Git pack shares a repository with other pack directories
- **THEN** LunaPack materializes the selected pack directory and its referenced content without materializing unrelated pack directories

#### Scenario: Preserve an immutable commit through an update

- **WHEN** `luna update` selects a pack from a Git source
- **THEN** LunaPack materializes the selected version from the commit resolved for that update operation
