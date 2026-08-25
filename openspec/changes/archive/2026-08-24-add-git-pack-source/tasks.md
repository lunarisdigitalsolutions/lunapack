## 1. Source Contracts And Persistence

- [x] 1.1 Extend `ProjectConfiguration` and source serialization with typed Git source fields (`url`, optional `ref`, optional repository-relative `path`, optional `timeoutSeconds`) while preserving existing local-source data.
- [x] 1.2 Extend `lunapack.schema.json`, `lunapack-lock.schema.json`, schema fixtures, and schema tests for Git sources, path/timeout validation, Git provenance, required `resolvedCommit`, and local-state compatibility.
- [x] 1.3 Extend catalog/discovered-pack and lock-state models so a selected Git pack carries repository URL, configured ref/path, resolved commit, and repository-relative pack root through lifecycle persistence.

## 2. Secure Git Transport And Cache

- [x] 2.1 Add a cross-platform installed-Git process runner using `ProcessStartInfo.ArgumentList`, no shell execution, bounded output capture, cancellation, timeout enforcement, and full process-tree cleanup.
- [x] 2.2 Implement Git ref resolution for explicit branches/commit SHAs and omitted refs via remote `HEAD`, including cached default-branch reuse and immutable commit validation.
- [x] 2.3 Implement versioned `.lunapack/git-sources/` metadata cache entries keyed by normalized URL/ref/path; atomically persist and invalidate discovered pack ID/version/repository-path data by resolved commit.
- [x] 2.4 Add unit coverage for process argument isolation, unavailable Git, non-zero exit, timeout/cancellation cleanup, source-path validation, ref resolution parsing, and cache hit/miss/corruption handling.

## 3. Git Catalog And Materialization Provider

- [x] 3.1 Implement Git-source discovery using shallow, filtered Git operations and tree inspection to list `pack.yml` files recursively under the optional source path, parse valid manifests, and exclude invalid candidates.
- [x] 3.2 Implement sparse materialization of each selected pack directory and manifest-referenced files at its resolved commit, with no history or unrelated pack directories and deterministic temporary-workspace cleanup.
- [x] 3.3 Integrate the Git provider into `PackCatalog` without changing semantic-version ranking, configured-source ordering, or local-source behavior.
- [x] 3.4 Add real-local-Git integration coverage for explicit branch and commit refs, default-branch resolution, path-scoped discovery, invalid manifests, cache reuse/refresh, shallow sparse materialization, and Git failure reporting.

## 4. CLI And Lifecycle Integration

- [x] 4.1 Add `luna source add git <repository-url> [--ref <branch-or-commit>] [--path <repository-relative-path>]`, typed duplicate detection, and configuration-preserving validation failures.
- [x] 4.2 Update install, update, graph resolution, dry-run, and preflight paths to materialize Git-selected packs from the operation's resolved commit and retain transactional failure behavior.
- [x] 4.3 Persist Git repository URL, configured ref/path, and `resolvedCommit` in `lunapack-lock.yml` for direct and composite Git-sourced packs; preserve existing local lock records.
- [x] 4.4 Add CLI, lifecycle, and integration tests for mixed-source precedence, Git composite references, lock provenance, update behavior, and no-mutation failures.

## 5. Documentation And Verification

- [x] 5.1 Create the next accepted ADR in `docs/internal/architecture/adr` for the installed-Git process boundary, sparse shallow transport, cache model, and immutable lock provenance; update its index.
- [x] 5.2 Update internal source-provider/lifecycle guidance and developer configuration, source-command, manifest, lock-file, and pack lifecycle references with Git source fields, timeout limits, cache behavior, Git prerequisite, and provenance examples.
- [x] 5.3 Run formatting, focused unit/integration suites, schema validation, and the full CLI test suite; verify local-source compatibility and run `openspec validate add-git-pack-source --type change --strict`.
