# Luna Links Design

## Context

See `proposal.md` for motivation and the delta specs for observable behavior.

LunaPack currently persists portable intent in `ProjectConfiguration`, resolved ownership in `ProjectLockFile`, and both documents atomically through `ProjectStateStore`. Pack dependency resolution produces a `ResolvedPackGraph`; installation and update planners turn graph-managed files into transactional actions. Those planners currently model ownership in terms of `ResolvedPack` and `DiscoveredPack`, while audit and command dispatch also assume every root is a versioned pack.

Local and Git sources already share configured-source identities. Git packs resolve refs through `GitRefResolver`, discover catalog metadata through `GitPackDiscovery`, and materialize sparse content through `GitPackMaterializer`. Existing Git catalog metadata is project-local under `.lunapack`; link source content has a separate cross-project user-cache contract.

The design must preserve Native AOT serialization, `System.IO.Abstractions` testability, `ProjectPath` path authority, existing pack behavior, and atomic managed-file rollback.

## Goals / Non-Goals

**Goals:**

- Add links without making catalog or dependency resolution understand project-owned definitions.
- Feed packs and links into one ownership, conflict, update-action, transaction, audit, and rollback path.
- Resolve every operation from an immutable source snapshot so hashed bytes equal planned and copied bytes.
- Keep configuration portable and lock state reproducible while retaining schema-version compatibility.
- Make Git cache reuse safe under corruption or local tampering.

**Non-Goals:**

- Generalize links into publishable package artifacts.
- Add a provider plugin contract beyond configured local and Git sources.
- Add cache eviction policy in the first release.
- Add link composition, parameters, rendering, scripts, or merge strategies.

## Decisions

### 1. Persist links as first-class project and lock models

Add `Links` mappings to `ProjectConfiguration` and `ProjectLockFile`. Configuration values contain only selectors and mapping intent. Lock values contain configured-source identity, definition digest, optional Git evidence, and selected-file ownership. Both mappings default empty so existing version-1 files remain valid and normalize without migration.

Schema version `1` remains because additions are optional and existing documents retain their meaning. Native AOT JSON/YAML contexts and model validation receive explicit link types. `ProjectStateStore` normalizes every path-bearing link field through `ProjectPath`; Git refs remain opaque strings.

Alternative: store links as requested packs and resolved packs. Rejected because fake package versions and pack paths would leak into public state and blur source-owned manifests with project-owned selections.

### 2. Generalize the lifecycle input after pack dependency resolution

Introduce an in-memory managed-root model containing owner kind, owner name, source evidence, and resolved managed files. Existing pack graphs adapt to this model after catalog and dependency resolution. `LinkResolver` produces the same model directly from one link definition. The installation planner, update planner, transaction, ownership map, audit, and lock assembly consume managed roots rather than assuming every owner is a pack.

Pack-specific stages remain before the adapter: catalog selection, semantic versions, references, parameters, templates, strategies, scripts, and trust. A link emits only copy/overwrite managed files and never enters catalog or dependency resolution. No temporary or synthetic `pack.yml` is written, and no fake semantic version is introduced.

Alternative: construct a synthetic `PackManifest` and `DiscoveredPack` for each link. Rejected because sentinel versions and pack-only fields could escape through output, hooks, or lock serialization.

### 3. Resolve links through source snapshots

Define a source-snapshot boundary that exposes normalized repository-relative file paths, selected file bytes, configured-source identity, and optional effective Git ref and commit. A local snapshot reads selected regular files into an operation-owned temporary snapshot before lifecycle planning. A Git snapshot resolves one commit, enumerates its tree, and materializes only selected blobs.

The resolver locates the configured source by exact case-sensitive name. Updates use the immutable source identity in lock state and fail if current configuration no longer provides that identity; changing a source definition does not silently redirect an installed link.

Alternative: let selection and copying read live source files independently. Rejected because a local source could change between hashing and copy, invalidating lock evidence.

### 4. Use one deterministic selection and mapping pipeline

`LinkSelectionService` normalizes source-relative inputs to `/`, rejects rooted and parent-traversal forms, and evaluates candidates with the existing `Microsoft.Extensions.FileSystemGlobbing` dependency. Exact files are direct matches; directories become recursive matches; patterns use glob matching. Includes form an ordinal set, exclusions filter that set, and final paths sort ordinally.

Local traversal does not follow directory symlinks or reparse points and rejects selected symbolic links. Git tree entries must be regular blobs. Every selected path is checked against its snapshot root after normalization.

Mapping runs after selection: calculate path relative to the link base, validate and remove `stripPrefix`, optionally reduce to the file name for `flatten`, prepend `target`, then call `ProjectPath.NormalizeProjectRelativePath` against the workspace. A map keyed by effective target detects flatten and other mapping collisions before ownership preflight.

Alternative: use ad hoc wildcard and string-prefix logic. Rejected because existing glob support and centralized path containment provide more consistent cross-platform behavior.

### 5. Canonicalize definition identity independently of YAML formatting

Compute `definitionSha256` from a fixed-order UTF-8 projection of semantic fields: link name, source name, normalized base path, ordinally sorted unique includes and excludes, normalized target, opaque ref, normalized strip prefix, and flatten flag. Omitted optional paths use one canonical empty representation. YAML key order, quoting, line endings, slash direction, and selector order therefore do not create false outdated results.

The locked configured-source identity is compared separately. This ensures a source URL or local path change cannot hide behind an unchanged source name or definition digest.

Alternative: hash serialized YAML. Rejected because formatting-only edits would mark links outdated.

### 6. Add a user-level immutable Git content cache

Add a platform cache-root resolver and a Git link cache beneath `sources/<source-identity-sha256>/<resolved-commit>/`. Commit metadata stores normalized tree paths and Git blob IDs; selected bytes are populated on demand. Git uses a shallow, blob-filtered operation repository to enumerate the committed tree and retrieve only missing selected blobs. Materialized bytes are copied into the operation snapshot before planning.

Cache entries are untrusted optimization. Reuse requires exact source identity and commit metadata, and selected bytes are verified against recorded Git blob IDs. Invalid or incomplete entries are discarded or repaired from Git. Cache writes use temporary paths and atomic rename. Cache failure may fail source resolution but never mutates project files or state.

The existing project-local Git catalog metadata remains unchanged because it serves pack discovery rather than reusable source content.

Alternative: cache full working trees. Rejected because it transfers and stores unrelated repository content. Alternative: reuse `.lunapack/git-sources`. Rejected because that cache is project-local metadata with a different lifecycle and trust boundary.

### 7. Compare resolved selections, not timestamps or commits alone

Create a resolved-link snapshot containing definition digest, source identity, optional commit, and files keyed by normalized source path. Diff snapshots by source path, declared target, effective target, and SHA-256 content. Pair one removed and one added path as a move only when their digest match is unique; ambiguous equal-content cases remain explicit additions and removals.

`outdated` performs resolution and diff without mutation. A new Git commit with an equivalent snapshot is current. An explicit named update may refresh locked commit evidence without file actions. Local timestamps may avoid re-reading unchanged candidates only when size and timestamp metadata point to a previously verified digest; final lifecycle decisions always use content hashes.

Pack update behavior remains unchanged. Link updates do not preserve an old effective target when the current definition maps that source file elsewhere; the planner removes the unchanged old target and creates the new one after full conflict preflight.

### 8. Dispatch names before entering pack catalog operations

Add one `links` command group with `add`, `list`, `show`, and governance-standard `rm` subcommands. `links rm` follows ADR-0048; `uninstall` remains the command that removes installed roots and managed content.

Install, named update, and uninstall load project state first. A matching configured or locked link dispatches to link resolution; otherwise existing pack behavior applies. Link creation rejects names already used by links or installed root packs, preventing installed-root ambiguity. `--force --install` on an installed link plans an update against prior lock ownership in the same transaction.

`links add --install` prepares the amended in-memory configuration, resolves and preflights content, applies file actions, then saves configuration and lock state together. `links rm --force` uses the transaction engine to delete only unchanged targets, preserves modified targets as explicitly unmanaged residue, and reports every preserved path.

### 9. Record the architectural boundary and user workflows

Implementation includes ADR-0051 for project-owned link definitions normalized into shared managed-root lifecycle planning. Internal architecture documentation explains source snapshot, cache, and ownership boundaries. Developer documentation separates link configuration reference from install/update/audit/removal how-to guidance. Product requirements describe links as copied managed files rather than filesystem symbolic links.

## Risks / Trade-offs

- [Shared lifecycle refactor regresses pack behavior] -> Keep pack resolution unchanged, adapt only after graph resolution, and run existing pack lifecycle suites alongside new cross-owner tests.
- [Glob behavior differs by operating system] -> Normalize candidate and pattern separators to `/`, compare ordinally, and cover Windows-style input plus Linux/macOS semantics.
- [Local source changes during resolution] -> Snapshot selected bytes before preflight and use snapshot bytes for both hashes and writes.
- [User cache is corrupted or tampered with] -> Verify metadata identity, commit, and blob IDs before reuse; repair from Git or fail without project mutation.
- [Large repositories consume memory or cache space] -> Enumerate path metadata first, materialize selected blobs only, stream hashing where practical, and defer eviction until usage data justifies policy.
- [Forced removal leaves unmanaged files] -> Preserve modified content by contract and report exact retained paths after ownership removal.
- [Older Luna versions cannot read projects after links are added] -> Document the minimum supporting CLI version; files without `links` remain backward-compatible.

## Migration Plan

1. Extend schemas, models, serializer contexts, validation, path normalization, and initialization with optional empty link collections.
2. Add deterministic definition hashing, source snapshots, selection/mapping, and Git link cache with focused tests.
3. Introduce managed-root adapters and expand shared ownership, planning, transaction, lock assembly, and audit paths while retaining all existing pack tests.
4. Add link configuration commands and lifecycle dispatch, then cover process-level local and Git workflows.
5. Publish ADR-0051 and update product, internal, developer, schema, and CLI reference documentation.

Rollback before a release removes the optional models and commands with no project migration. After release, rollback requires uninstalling links with the supporting CLI before downgrading; an older CLI is not expected to accept project or lock documents containing link state.
