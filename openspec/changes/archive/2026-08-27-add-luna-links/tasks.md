# Luna Links Implementation Tasks

## 1. State And Schema Contracts

- [x] 1.1 Extend `lunapack.yml` schema and examples with optional link definitions, safe path constraints, unique selectors, pack-ID-shaped names, and compatibility coverage for version-1 files without links.
- [x] 1.2 Extend `lunapack-lock.yml` schema and examples with resolved link source identity, definition digest, Git evidence, and per-file source/target/hash records, including compatibility and incomplete-record tests.
- [x] 1.3 Add project and lock link models to Native AOT JSON/YAML serialization contexts, model validation, and cloning paths; verify unknown pack-only link properties are rejected.
- [x] 1.4 Normalize every persisted link path through `ProjectPath`, preserve opaque Git refs, and add Windows-separator, rooted-path, traversal, and canonical-output tests.
- [x] 1.5 Initialize empty link collections in new project configuration and lock files while loading omitted collections as empty; update initialization and state-store tests.
- [x] 1.6 Implement canonical semantic definition hashing and tests proving formatting, slash, and selector-order changes are stable while behavioral changes alter the digest.

## 2. Shared Managed-Root Lifecycle

- [x] 2.1 Introduce the in-memory managed-root and managed-file ownership model with explicit pack/link owner kinds and source evidence.
- [x] 2.2 Adapt resolved pack graphs into managed roots after pack-specific resolution without changing catalog, dependency, parameter, template, strategy, script, or trust behavior.
- [x] 2.3 Refactor installation ownership preflight to include pack and link lock records, preserve explicit adoption behavior, and reject cross-root target collisions.
- [x] 2.4 Refactor update planning, transaction rollback, and lock assembly to consume managed roots while preserving declared/effective pack target semantics.
- [x] 2.5 Run and extend focused pack lifecycle regression tests for install, update, remapping, adoption, hooks, audit, uninstall, and rollback after the shared abstraction change.

## 3. Link Selection And Local Resolution

- [x] 3.1 Implement deterministic include union, recursive directory expansion, glob matching, post-include exclusions, deduplication, ordinal ordering, and empty-selection validation.
- [x] 3.2 Implement target mapping for base paths, workspace-root defaults, targets, strip prefixes, flattening, and duplicate target detection through `ProjectPath` containment checks.
- [x] 3.3 Add local-source enumeration that selects regular files only, does not follow symlinks or reparse points, and rejects physical or normalized source-root escapes.
- [x] 3.4 Snapshot selected local file bytes before planning so hashing and copying use identical content; add mutation-during-resolution and cleanup tests.
- [x] 3.5 Implement `LinkResolver` to bind exact configured source names and emit resolved managed roots plus lock-ready source and file provenance.

## 4. Git Resolution And Cache

- [x] 4.1 Add the platform-specific user cache-root resolver and source-identity/commit directory layout with Windows, Linux XDG/fallback, and macOS tests.
- [x] 4.2 Extend Git ref resolution for link overrides, configured refs, and remote `HEAD`, retaining one immutable commit through each operation.
- [x] 4.3 Enumerate regular Git tree blobs at the resolved commit and evaluate link selectors without requiring `pack.yml` or transferring repository history.
- [x] 4.4 Materialize only selected Git blobs into operation snapshots and persist cache metadata and content with temporary-path atomic writes.
- [x] 4.5 Verify cached source identity, commit metadata, and selected bytes against Git blob IDs; repair or reject corrupt and incomplete entries without project mutation.
- [x] 4.6 Add Git process tests for ref override/inheritance, unresolved refs, repositories without manifests, partial selection, cache reuse, commit isolation, timeout, cancellation, and cleanup.

## 5. Link Lifecycle Services

- [x] 5.1 Implement transactional link installation, duplicate-install detection, pack/link ownership conflicts, explicit adoption, resolved lock creation, and `--force --install` update behavior.
- [x] 5.2 Implement resolved-link diffing for definition, source path, declared/effective target, and SHA-256 changes, including unique same-digest move classification.
- [x] 5.3 Implement named link updates through locked source identity with added, changed, moved, and removed file actions plus commit-only evidence refresh.
- [x] 5.4 Extend outdated detection to links and report reasoned selection or definition changes while ignoring content-equivalent Git commits.
- [x] 5.5 Extend audit to report missing, modified, and conflicting link-owned targets without mutation.
- [x] 5.6 Implement digest-protected link uninstall that removes the definition, unchanged targets, and lock state atomically while preserving all state on modification failure.
- [x] 5.7 Implement forced definition removal that deletes unchanged targets, preserves and reports modified targets, and atomically removes definition and ownership records.

## 6. CLI Commands And Output

- [x] 6.1 Register `luna links add` with required/repeatable options, aliases, workspace handling, source/name validation, force replacement, and atomic `--install` dispatch.
- [x] 6.2 Register `luna links list` and format name, source, effective target, and installation status with deterministic ordering.
- [x] 6.3 Register `luna links show` and format definition, effective ref, resolved commit, status, selected-file count, and locally modified-file count.
- [x] 6.4 Register ADR-0048-compliant `luna links rm`, including installed-link refusal guidance and forced-removal output.
- [x] 6.5 Dispatch `install`, named `update`, and `uninstall` to configured or locked links before pack catalog handling without changing non-link command behavior.
- [x] 6.6 Include links in `outdated` and `audit` output and update contextual next-step guidance and CLI help contract tests.

## 7. End-To-End Verification

- [x] 7.1 Add local-link process tests covering exact files, recursive directories, glob unions, exclusions, base paths, targets, strip prefixes, flattening, and empty or colliding selections.
- [x] 7.2 Add local lifecycle process tests covering add/install, list/show, source changes, outdated reasons, update, audit, uninstall, force removal, and locally modified targets.
- [x] 7.3 Add Git-link process tests covering ref overrides, immutable commit locking, selected-file changes, content-equivalent commits, cache reuse, and repositories without LunaPack manifests.
- [x] 7.4 Add transaction and security process tests for traversal, symlinks, unresolved refs, source identity changes, pack/link ownership conflicts, invalid persisted state, and save rollback.
- [x] 7.5 Verify every project and lock state mutation persists `/` paths and retains unrelated sources, packs, links, variables, remapping, trust, and ownership.

## 8. Documentation And Release Validation

- [x] 8.1 Create ADR-0051 from the repository template for project-owned links normalized into shared managed-root lifecycle planning and add it to the ADR index.
- [x] 8.2 Update internal architecture and path-handling documentation with link resolution, source snapshots, cache trust, ownership, transaction, and compatibility boundaries.
- [x] 8.3 Update product requirements to define links as project-owned copied-file selections and preserve the stated non-goals.
- [x] 8.4 Add developer reference and how-to documentation for link configuration, selection/mapping semantics, Git cache locations, lifecycle commands, inspection, conflicts, and recovery; include the exact `github/awesome-copilot` examples for the `agents-csharp-expert` single-file link and `agents-ai-team` glob link, then update navigation.
- [x] 8.5 Add the externally observable Luna Links feature and minimum supporting CLI version to `CHANGELOG.md`.
- [x] 8.6 Run schema validation, CSharpier verification, focused unit and integration suites, the full CLI test suite, strict OpenSpec validation, and Release Native AOT publish validation.
