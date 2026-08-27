# Git Pack Sources Delta Specification

## ADDED Requirements

### Requirement: Resolve Git-backed links at immutable commits

For every Git-backed link resolution, LunaPack SHALL use the link's `ref` override when present and the configured source ref otherwise. When neither is present, it SHALL resolve remote `HEAD`. LunaPack SHALL resolve the effective ref to one immutable commit before evaluating selectors or reading content and SHALL use that commit consistently for the complete operation. A missing or unresolved ref SHALL produce a non-success result without changing project files or state.

#### Scenario: Override a configured source ref

- **WHEN** a link declares a ref different from its configured Git source
- **THEN** LunaPack selects files from the commit resolved from the link ref and records that effective ref and commit

#### Scenario: Inherit a configured source ref

- **WHEN** a Git-backed link omits its own ref and its source declares one
- **THEN** LunaPack resolves and uses the configured source ref

#### Scenario: Reject an unresolved ref

- **WHEN** the effective Git ref cannot be resolved to a commit
- **THEN** LunaPack returns a non-success result without changing project files, configuration, lock state, or cache metadata

### Requirement: Materialize only selected Git link content

LunaPack SHALL enumerate and obtain the files needed to evaluate and materialize a link at its resolved commit without transferring Git history. It SHALL not require, discover, or persist a `pack.yml` for link content. It SHALL not materialize unrelated repository content into the consuming workspace.

#### Scenario: Install content from a repository without pack manifests

- **WHEN** a Git repository contains matching files but no `pack.yml`
- **THEN** LunaPack can install the selected files through the configured link

#### Scenario: Exclude unrelated repository content

- **WHEN** a link selects a subset of files from a larger repository
- **THEN** LunaPack copies only the selected files to the workspace

### Requirement: Cache immutable Git link source content

LunaPack SHALL cache Git link content by stable configured-source identity and resolved commit under the operating system's user cache location: `%LOCALAPPDATA%\LunaPack\cache\sources` on Windows, `$XDG_CACHE_HOME/lunapack/sources` or `~/.cache/lunapack/sources` on Linux, and `~/Library/Caches/LunaPack/sources` on macOS. Cache reuse SHALL require an exact source identity and commit match. Cache contents SHALL remain generated source material rather than project configuration or lock state.

#### Scenario: Reuse an immutable cached commit

- **WHEN** a later link operation resolves the same configured-source identity and commit already present in cache
- **THEN** LunaPack reuses validated cached content without changing resolved behavior

#### Scenario: Isolate commits in cache

- **WHEN** one source resolves to a new commit
- **THEN** LunaPack stores or reuses content under that commit without replacing content cached for the prior commit
