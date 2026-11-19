# git-pack-sources Specification

## Purpose

Enable consumers to discover and install versioned engineering packs from a remote Git repository with reproducible, commit-pinned source evidence.

## Requirements

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

### Requirement: Resolve Git-backed links at immutable commits

For every Git-backed link resolution, LunaPack SHALL use the link's `ref`
override when present and the configured source ref otherwise. When neither is
present, it SHALL resolve remote `HEAD`. LunaPack SHALL resolve the effective
ref to one immutable commit before evaluating selectors or reading content and
SHALL use that commit consistently for the complete operation. A missing or
unresolved ref SHALL produce a non-success result without changing project files
or state.

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

LunaPack SHALL enumerate and obtain the files needed to evaluate and materialize
a link at its resolved commit without transferring Git history. It SHALL not
require, discover, or persist a `pack.yml` for link content. It SHALL not
materialize unrelated repository content into the consuming workspace.

#### Scenario: Install content from a repository without pack manifests

- **WHEN** a Git repository contains matching files but no `pack.yml`
- **THEN** LunaPack can install the selected files through the configured link

#### Scenario: Exclude unrelated repository content

- **WHEN** a link selects a subset of files from a larger repository
- **THEN** LunaPack copies only the selected files to the workspace

### Requirement: Cache immutable Git link source content

LunaPack SHALL cache Git link content by stable configured-source identity and
resolved commit under the operating system's user cache location:
`%LOCALAPPDATA%\LunaPack\cache\sources` on Windows,
`$XDG_CACHE_HOME/lunapack/sources` or `~/.cache/lunapack/sources` on Linux, and
`~/Library/Caches/LunaPack/sources` on macOS. Cache reuse SHALL require an exact
source identity and commit match. Cache contents SHALL remain generated source
material rather than project configuration or lock state.

#### Scenario: Reuse an immutable cached commit

- **WHEN** a later link operation resolves the same configured-source identity and commit already present in cache
- **THEN** LunaPack reuses validated cached content without changing resolved behavior

#### Scenario: Isolate commits in cache

- **WHEN** one source resolves to a new commit
- **THEN** LunaPack stores or reuses content under that commit without replacing content cached for the prior commit

### Requirement: Normalize source identity and enforce fingerprint uniqueness

LunaPack SHALL derive a source fingerprint independently of its configured identifier. A Git fingerprint SHALL combine a sanitized normalized repository identity, canonical Git ref, and normalized repository-relative base path. Supported HTTPS, SSH, and scp-style URLs for the same repository SHALL share an identity; transport and embedded credentials SHALL not be part of it. For `github.com`, host, owner, and repository names SHALL be lowercase and an optional `.git` suffix and trailing separators SHALL be removed. The base path SHALL use `/`, collapse redundant separators and `.` segments, reject escaping `..` segments, omit unnecessary trailing separators, and represent the repository root as `/`. A local workspace source fingerprint SHALL use its canonical filesystem path and SHALL remain distinct from every Git fingerprint. `lunapack.yml` SHALL contain at most one source for each fingerprint.

#### Scenario: Match GitHub HTTPS and SSH forms

- **WHEN** two Git declarations identify the same GitHub repository, canonical ref, and base path through supported HTTPS and SSH URL forms
- **THEN** LunaPack computes the same fingerprint for both declarations

#### Scenario: Distinguish source roots

- **WHEN** two Git declarations use the same repository and canonical ref but different normalized base paths
- **THEN** LunaPack computes different fingerprints

#### Scenario: Reject duplicate configured fingerprints

- **WHEN** two workspace source identifiers normalize to the same fingerprint
- **THEN** LunaPack rejects the workspace configuration before catalog or lifecycle mutation

#### Scenario: Keep local and Git sources distinct

- **WHEN** a local source points at a checkout of a configured Git repository
- **THEN** LunaPack computes different local and Git fingerprints

### Requirement: Canonicalize requested Git refs

LunaPack SHALL preserve an exact commit as an immutable ref identity and SHALL canonicalize a resolvable symbolic ref to its complete Git ref before fingerprint matching or persistence for a pack-defined source. A short branch name SHALL canonicalize to `refs/heads/<name>` and a short tag name SHALL canonicalize to `refs/tags/<name>`. LunaPack SHALL reject a short ref that resolves to both a branch and a tag and SHALL require the complete ref. Workspace Git sources MAY omit a ref and SHALL retain existing remote-HEAD behavior; a source without an unambiguous effective ref SHALL not satisfy a pack-defined source requirement.

#### Scenario: Canonicalize a branch

- **WHEN** `main` resolves only as a remote branch
- **THEN** LunaPack uses `refs/heads/main` in the source fingerprint and persisted pack-defined source

#### Scenario: Reject an ambiguous short ref

- **WHEN** a short ref resolves to both a branch and a tag
- **THEN** LunaPack returns a non-success result that identifies both complete ref choices

#### Scenario: Distinguish commit and branch identities

- **WHEN** a branch and an exact commit currently resolve to the same commit
- **THEN** LunaPack computes different fingerprints because their update semantics differ

### Requirement: Materialize external source content safely

LunaPack SHALL resolve each approved external Git source to an immutable commit before reading selected content. It SHALL confine source paths and followed symbolic links to the resolved source root, SHALL never execute external-source content, and SHALL use the existing Git timeout, cancellation, credential, and process-isolation rules. Equivalent source fingerprints at the same resolved commit SHALL share reconstructable cache content keyed by fingerprint and commit, regardless of URL transport or pack alias.

#### Scenario: Reuse one cached external source

- **WHEN** multiple packs require equivalent source fingerprints that resolve to the same commit
- **THEN** LunaPack may materialize one cache entry and use it for every mapped pack alias

#### Scenario: Reject source-root escape

- **WHEN** an external selector or followed symbolic link resolves outside the approved source root
- **THEN** LunaPack returns a non-success result before changing project configuration, managed files, or lock state

#### Scenario: Keep credentials out of persisted identity

- **WHEN** source access uses credentials supplied by supported Git credential handling
- **THEN** LunaPack omits those credentials from fingerprints, configuration, lock state, console output, audit output, and logs

### Requirement: Deny insecure Git transports by default

LunaPack SHALL deny Git network operations using `http://` or `git://` when
`git.allow-insecure-transport` resolves to `false`. It SHALL deny the operation
before network access and without changing project configuration, lock state,
or catalog cache. This policy SHALL apply to source addition, ref resolution,
catalog discovery, pack materialization, Git-backed links, and external Git
sources.

#### Scenario: Block an insecure source without an override

- **WHEN** a Git operation targets an `http://` or `git://` source and the
  effective preference is `false`
- **THEN** LunaPack returns a non-success result before network access and
  identifies `git.allow-insecure-transport` as the opt-in

#### Scenario: Preserve secure and local Git sources

- **WHEN** a Git operation targets an HTTPS, SSH, scp-style, or local source
- **THEN** the insecure-transport preference does not block the operation

### Requirement: Prevent secure Git transport bypasses

LunaPack SHALL block Git URL rewrites from a secure source to `http://` or
`git://`. Secure HTTPS Git operations SHALL NOT follow redirects, so an HTTPS
source cannot downgrade to plaintext through a redirect. Secure HTTPS
operations SHALL require certificate verification. This also means an
HTTPS-to-HTTPS redirect fails.

#### Scenario: Block an insecure URL rewrite

- **WHEN** Git configuration rewrites an HTTPS source to `http://` or `git://`
- **THEN** Git blocks the rewritten transport before connecting to it

#### Scenario: Do not follow an HTTPS redirect

- **WHEN** an HTTPS Git server responds with a redirect
- **THEN** LunaPack fails the operation without following the redirect

#### Scenario: Reject disabled HTTPS verification

- **WHEN** inherited Git configuration disables HTTPS certificate verification
- **THEN** LunaPack overrides the setting or fails closed before network access

#### Scenario: Do not interpret a source as a Git option

- **WHEN** a repository location begins with `-` or uses unsafe scp-style
  syntax
- **THEN** LunaPack rejects it before invoking Git

#### Scenario: Reject an external smudge transport

- **WHEN** repository attributes select a smudge filter, including Git LFS
- **THEN** LunaPack fails before checkout and no smudge process or external
  endpoint is invoked

#### Scenario: Reject a hosted local-filesystem transport

- **WHEN** a configured or rewritten network source uses UNC or a hosted
  `file://` location
- **THEN** LunaPack fails before filesystem or network access

#### Scenario: Reject ambiguous URL-specific configuration

- **WHEN** a Git repository URL contains a query or fragment
- **THEN** LunaPack rejects the URL before network access

#### Scenario: Preserve exact effective URL semantics

- **WHEN** Git resolves or verifies a secure source URL
- **THEN** LunaPack preserves the exact URL bytes, rejects surrounding
  whitespace or undecodable output, and uses the same repository context as the
  requested operation

#### Scenario: Bind HTTPS settings without normalization drift

- **WHEN** an HTTPS repository location contains a raw backslash or any value
  Git matches differently from a normalized .NET URI
- **THEN** LunaPack rejects it, or binds and verifies security settings using the
  exact effective URL text

### Requirement: Warn when insecure Git transport is enabled

When `git.allow-insecure-transport` resolves to `true`, LunaPack SHALL warn once
per insecure source per CLI operation before network access. The warning SHALL
identify the source without exposing URL credentials.

#### Scenario: Use an opted-in insecure source

- **WHEN** a Git operation targets an `http://` or `git://` source and the
  effective preference is `true`
- **THEN** LunaPack warns before network access and may proceed

### Requirement: Keep transport policy separate from Git source identity

LunaPack SHALL continue deriving Git source identity independently of transport
policy. Denying or allowing an insecure URL SHALL NOT rewrite its configured
URL or change its normalized source fingerprint or lock ownership identity.

#### Scenario: Deny transport without changing source identity

- **WHEN** an insecure Git source is denied by the effective preference
- **THEN** LunaPack leaves the configured URL and persisted source identity
  unchanged

### Requirement: Diagnose Git authentication through existing credentials

Git authentication for secure transports SHALL continue using Git Credential
Manager, credential helpers, SSH configuration/agent, and supported environment
configuration. For plaintext HTTP operations enabled by the preference,
LunaPack SHALL NOT invoke Git credential helpers. Luna SHALL NOT collect or
store Git passwords. Authentication failures SHALL identify the source,
recommend its Git credential manager or SSH configuration, and explain local
ignoring as an option.

#### Scenario: Fail a private Git clone

- **WHEN** source `engineering` fails Git access because authentication was
  rejected
- **THEN** Luna reports `Unable to authenticate to Git source 'engineering'`,
  recommends Git credential-manager/SSH authentication, and mentions
  `luna sources ignore engineering` without prompting for a Git password

#### Scenario: Skip credential helpers for opted-in plaintext HTTP

- **WHEN** an HTTP Git operation is enabled by the preference and Git
  credential helpers are configured
- **THEN** LunaPack does not invoke those helpers for the operation

### Requirement: Classify Git failures without hiding independent sources

Git authentication, permission, timeout, unavailable executable, and network
failures SHALL remain distinguishable without leaking process secrets.
Optional failed Git sources SHALL not block accessible configured sources.
Required Git sources SHALL still fail the affected operation without mutation.

#### Scenario: Search after a Git authentication failure

- **WHEN** a Git source cannot authenticate but local and OCI sources are accessible
- **THEN** Luna warns for the Git source and retains the other catalog results

#### Scenario: Require an inaccessible locked Git source

- **WHEN** update needs a pack's locked Git source and authentication fails
- **THEN** Luna fails that update with Git authentication guidance and preserves
  files and project state
