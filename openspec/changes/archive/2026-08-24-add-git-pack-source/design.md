## Context

See [proposal.md](proposal.md) for motivation and the Git-source delta specs for behavior. The CLI currently deserializes a local-source-only configuration model, dispatches catalog browsing directly to `LocalPackDiscovery`, and treats local filesystem paths as both catalog location and pack materialization root. Resolved lock records only retain `sourcePath` and `packPath`.

Git sources add a remote transport boundary while existing catalog ranking, manifest validation, dependency resolution, lifecycle preflight, and transactional state writes remain shared. The implementation must run on every supported .NET platform, use no Git-client package, avoid command-shell construction, preserve version-1 local configuration and lock files, and avoid downloading history or unrelated pack content.

## Goals / Non-Goals

**Goals:**

- Extend `lunapack.yml` with a validated `git` source variant, CLI registration, and a five-minute-or-less per-source Git timeout.
- Resolve each source operation to one immutable commit, then preserve that evidence in catalog entries and the lock file.
- Discover only `pack.yml` candidates under the selected repository path; materialize only selected pack directories and files required by their manifests.
- Reuse persisted discovery metadata when a remote source resolves to the cached commit.
- Keep provider-specific transport behind a narrow boundary so catalog and lifecycle selection rules remain unchanged.

**Non-Goals:**

- Git authentication, credential storage, SSH-agent setup, or provider-host-specific APIs.
- Offline use of stale catalogs when the remote commit cannot be resolved.
- Git worktree reuse, full repository mirrors, history browsing, submodule support, or arbitrary repository-file downloads.
- Changes to pack-manifest format, semantic-version precedence, source ordering, or existing local-source behavior.

## Decisions

### Model sources as a discriminated configuration union

Replace the local-only source model with local and Git variants. A Git entry uses `type: git`, `url`, optional `ref`, optional repository-relative `path`, and optional `timeoutSeconds`; omitted timeout resolves to 300 seconds and values outside 1 through 300 are rejected. `luna source add git <url> [--ref <ref>] [--path <path>]` persists those portable inputs. The configuration schema remains at version 1 because existing valid documents retain their meaning and new fields appear only in a new source variant.

Use explicit Git provenance fields in a resolved lock record rather than encoding URL, ref, or commit into the existing filesystem `sourcePath` field. Keep local lock records unchanged; Git records add source type, repository URL, configured ref/path when present, and `resolvedCommit`.

Alternative considered: a generic string-to-string source options map. Rejected because schema validation, duplicate detection, documentation, and future source contracts need typed fields.

### Introduce a Git provider behind the catalog/materialization boundary

Evolve `PackCatalog` source dispatch so local and Git providers return common catalog candidates augmented with provider-specific materialization evidence. The Git provider owns ref resolution, manifest listing, cache lookup/write, temporary workspace setup, and content materialization. `PackCatalog`, graph resolution, ranking, and lifecycle planners continue to consume the common candidate representation and source order.

The provider resolves a ref once per browse/resolve operation. With an explicit ref it resolves that ref remotely; without one it resolves remote `HEAD` to determine the default branch and commit. Cached default-branch metadata permits later default-ref checks to query the cached branch directly, while cache invalidation remains driven by the resolved commit SHA.

Alternative considered: cloning a repository into every configured source path and reusing local discovery. Rejected because it conflates portable source configuration with volatile transport state and downloads unrelated content.

### Wrap the installed Git executable without a shell

Create one process-runner abstraction for Git. It uses `ProcessStartInfo.FileName = "git"`, `UseShellExecute = false`, redirected standard output/error, and `ArgumentList` for every argument. It enforces the source timeout with cancellation, terminates the full child process tree on timeout or cancellation, captures bounded diagnostic output, and maps unavailable executable, non-zero exit, timeout, and cancellation to actionable failures. Repository URLs, refs, and paths never form a shell command string.

Use a temporary directory per operation beneath the project cache area. Clean it in `finally`; cache writes occur only after complete, schema-valid discovery. Tests use a real locally initialized Git repository and the process runner boundary, not a Git NuGet client.

Alternative considered: a Git library package or `cmd.exe`/`sh` command strings. Rejected by the source constraint and because shell quoting differs across operating systems.

### Use shallow filtered sparse workspaces for discovery and materialization

For each resolved commit, initialize a temporary Git workspace, fetch only depth one with blob filtering, and inspect the commit tree using Git commands. Discovery lists paths from that tree, filters to `pack.yml` under the configured source path, and checks out only those manifest paths to parse and validate them. Installation and update reconfigure sparse checkout for each selected pack directory, then read its manifest-referenced content from the same resolved commit. No operation requests history; temporary workspaces are removed after the operation.

The provider passes `--` before pathspec arguments and independently validates configured paths as repository-relative paths without `..` traversal. If the Git server cannot satisfy the shallow filtered operation, the command fails before project mutation rather than falling back to a full repository download.

Alternative considered: `git archive --remote`. Rejected because server support is optional and it cannot reliably support all required ref-resolution and catalog operations.

### Persist compact Git source metadata under `.lunapack`

Store one JSON cache entry per normalized source identity under `.lunapack/git-sources/`, using a SHA-256 filename derived from canonical URL, ref, and path values. Each entry contains cache format version, identity, cached default branch when applicable, resolved commit, and discovered pack ID/version/repository-relative path data. Do not persist credentials, command lines, Git objects, or pack content.

On a later operation, resolve the remote commit first. If it matches the cache entry, reconstruct catalog candidates from cached metadata; otherwise rediscover and atomically replace the entry. Invalid, unreadable, or incompatible cache entries are ignored and rebuilt. Cache cleanup is limited to the source's temporary workspace; cache eviction policy is out of scope.

Alternative considered: serializing the whole catalog or caching a local clone. Rejected because catalogs include nonportable process state and a clone retains unrelated content/history.

## Risks / Trade-offs

- [Remote Git servers or local Git clients do not support shallow filtered fetches] -> Fail before lifecycle mutation, explain the capability requirement, and cover compatible local-Git integration tests.
- [A branch changes while cataloging] -> Resolve once to a commit and use that SHA for all reads in the operation; persist it for installed packs.
- [Timeout leaves child processes or temporary files] -> Kill the complete process tree, await process exit, and remove operation directories in `finally`.
- [Cache becomes stale or corrupt] -> Verify source identity and resolved commit; treat unreadable/invalid metadata as a cache miss and replace atomically only after successful discovery.
- [Source values contain adversarial shell/path syntax] -> Use argument lists, no shell, `--` pathspec separators, repository-relative path validation, and integration tests with special characters.
- [Lock schema grows while local records remain deployed] -> Add an optional Git provenance object/fields only for Git records and retain current local-source validation cases.

## Migration Plan

1. Extend models, schemas, serializers, CLI registration, provider integration, and lock persistence while retaining schema version 1 local records.
2. Add unit and real-Git integration coverage for schema validation, command parsing, default-branch and explicit-ref resolution, sparse materialization, cache hit/miss, timeout, failure atomicity, and provenance.
3. Update developer configuration, command, manifest, lifecycle, and source-provider documentation; add an ADR describing the installed-Git process boundary and immutable Git provenance.
4. Existing projects need no migration. Users add Git sources with `luna source add git`; `.lunapack` metadata is regenerated when absent.
5. Roll back by removing Git sources from `lunapack.yml`; existing local sources and lock records remain readable. Delete `.lunapack/git-sources/` to force metadata rebuild without changing installed managed files.
