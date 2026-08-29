# Understand Git source behavior

Git pack sources use the installed Git client for remote access, keep catalog
metadata in the project, and materialize repository content only for the current
operation. Use these details when diagnosing stale discovery, authentication,
or disk usage. They are not required for a first installation.

## Separate intent, resolved state, and cache

A configured source in `lunapack.yml` records its URL and optional `ref`,
repository `path`, and timeout. Installed pack records in `lunapack-lock.yml`
retain the exact resolved commit and source identity.

Luna stores catalog metadata below:

```text
<workspace>/.lunapack/git-sources/<source-fingerprint-sha256>.json
```

Each JSON entry contains source identity, resolved commit, remembered default
branch, discovered pack manifests, pack IDs, versions, and repository paths. It
does not contain a repository clone, selected pack files, or credentials. This
project-local metadata cache differs from the user-level content cache used by
Luna Links.

Deleting a Git-source metadata entry forces Luna to rebuild it on the next
source query. Do not commit or share `.lunapack`; treat it as untrusted generated
cache content.

## Resolve refs and discover manifests

Luna first resolves the configured `ref` to an exact commit with
`git ls-remote`. A source without `ref` resolves remote `HEAD` and remembers its
default branch. Later queries try that remembered branch directly. If a
repository changes its default branch while the previous branch still exists,
Luna continues following the previous branch. Configure an explicit `ref` when
default-branch changes must not alter or strand source behavior.

When the resolved commit equals the cached commit, Luna reuses cached catalog
metadata. When it changes or no valid cache exists, Luna performs discovery in
a temporary repository:

1. Initialize an empty repository.
2. Fetch the exact commit with depth one and `--filter=blob:none`.
3. List paths recursively below the configured source `path` with `git ls-tree`.
4. Read candidate `pack.yml` blobs with `git show`.
5. Validate each manifest against file paths listed in that tree.
6. Replace the project-local catalog metadata entry.

Invalid manifests are excluded from the catalog. Discovery reads manifest blobs
and tree paths; it does not check out every repository file.

## Materialize one selected pack

Installing, updating, validating, or inspecting selected content needs pack
files rather than catalog metadata. Luna creates another temporary repository,
fetches the exact resolved commit with depth one and blob filtering, limits a
sparse checkout to the selected pack path, and checks out detached
`FETCH_HEAD`. Luna then copies selected pack content into its protected
operation snapshot before planning files or hooks.

Discovery workspaces use:

```text
<system-temp>/lunapack/git-sources/temporary/<guid>
```

Pack, hook, and external-content operations use:

```text
<system-temp>/lunapack/operations/<guid>
```

Normal completion and handled failures remove these directories on a
best-effort basis. A terminated process or cleanup failure can leave a stale
workspace. Luna has no cache-clean command; remove stale directories manually
only after confirming no Luna process is using them.

Fetch depth, partial-clone filtering, sparse-checkout patterns, detached
checkout, cache naming, and temporary roots are fixed. Source URL, `ref`,
repository `path`, and `timeoutSeconds` are consumer-controlled.

## Authenticate through Git

Luna starts the `git` executable directly. It does not add authorization
headers, implement login or token refresh, choose a credential helper, or keep a
separate credential store. Git selects authentication from the source URL and
available user or process configuration, including HTTPS credential helpers,
SSH configuration and agents, and environment-based Git settings.

A full Git source can use an HTTPS, SSH, scp-style, or absolute local repository
URL. GitHub shorthand is converted to an HTTPS URL, so it follows HTTPS rather
than SSH authentication. Test access with the same operating-system user and
environment that will invoke Luna. Unattended jobs must configure
noninteractive Git authentication before running Luna.

Luna captures bounded Git standard output and error output and reports Git
failures. Those diagnostics can contain private URLs, usernames, paths, or
credential-helper messages. Never put secrets in source URLs, manifests,
command arguments, or committed project state, and redact diagnostics before
sharing them.

See [Add a pack source](../sources.md) for setup,
[Automation](../automation.md) for CI isolation, and the
[Threat model](../threat-model.md) for credential boundaries.
