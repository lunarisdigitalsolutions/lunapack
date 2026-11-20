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

Before checkout, Luna resolves the effective Git `filter` attribute for every
path in the selected commit and rejects any value that would run a smudge
process. This includes nested attributes, inherited attribute files, attribute
macros, and Git LFS. Store pack files as regular Git blobs; Luna does not follow
external smudge endpoints during materialization. This check uses a temporary
Git index and does not replace the index used for the actual checkout.

## Require secure transport

Luna rejects Git network operations that use `http://` or `git://` by default,
before connecting to the remote. HTTPS, SSH, scp-style, and local repositories
remain available. Git configuration that rewrites a secure source to `http`
or `git` is blocked as well.

To keep using a legacy plaintext source, set
`git.allow-insecure-transport` to `true` with `luna config`. For example:

```powershell
luna config set git.allow-insecure-transport true --global
```

Use `--project` or `--user-project` for a narrower scope. Project settings
override user-project settings, which override global settings; an explicit
`false` blocks plaintext transports even when a broader scope enables them.
The value applies to all insecure Git sources in its effective scope, not one
source. Luna warns once for each opted-in insecure source used during a CLI
operation. Prefer switching the source to HTTPS or SSH, then remove the
preference or set it to `false`.

Luna resolves Git `url.*.insteadOf` rules without contacting the remote, then
applies its HTTPS policy to the resulting URL. It requires certificate
verification and disables redirects for every secure HTTPS operation, including
an HTTPS-to-HTTPS rewrite. `GIT_SSL_NO_VERIFY` and URL-specific
`http.sslVerify=false` cannot disable verification. If inherited Git
configuration would override the enforced redirect or certificate setting, Luna
fails before network access.

A remote that redirects, including to another HTTPS URL, will fail; configure
its final HTTPS URL instead. This prevents an HTTPS source from redirecting to
plaintext HTTP.

## Authenticate through Git

Luna starts the `git` executable directly. It does not add authorization
headers, implement login or token refresh, choose a credential helper, or keep a
separate credential store. Git selects authentication from the source URL and
available user or process configuration, including HTTPS credential helpers,
SSH configuration and agents, and environment-based Git settings. For an
opted-in `http://` source, Luna disables configured Git credential helpers.
This does not remove credentials embedded in a URL or credentials supplied by
`http.extraHeader` or proxy configuration. Do not use plaintext HTTP for private
repositories.

A full Git source can use an HTTPS, SSH, scp-style, or absolute local repository
URL without a query or fragment. UNC paths and hosted `file://` locations are
not supported. GitHub shorthand is converted to an HTTPS URL, so it follows
HTTPS rather than SSH authentication. Test access with the same
operating-system user and environment that will invoke Luna. Unattended jobs
must configure noninteractive Git authentication before running Luna.

Repository locations cannot begin with `-` or contain leading or trailing
whitespace. HTTPS locations cannot contain raw backslashes. Luna validates
scp-style user and host syntax before running Git so a source cannot be
interpreted as a Git command option. Git remote-helper syntax, such as
`ext::command`, is not supported.

Luna captures bounded Git output and reports curated guidance for failed
commands instead of echoing raw Git errors. Git configuration and external
process tracing can still expose credentials. Avoid secrets in source URLs,
manifests, command arguments, or committed project state, and redact captured
logs before sharing them.

See [Add a pack source](../sources.md) for setup,
[Automation](../automation.md) for CI isolation, and the
[Threat model](../threat-model.md) for credential boundaries.
