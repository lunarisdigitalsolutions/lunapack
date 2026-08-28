# Pack-Defined External Git Sources Design

## Context

See [proposal.md](proposal.md) for motivation and scope. LunaPack currently models workspace local and Git sources in `ProjectConfiguration`, stores pack-relative file selectors directly in `PackManifest`, resolves composite packs before lifecycle planning, materializes Git pack content through the installed Git client, and applies managed-file mutations through `PackUpdateTransaction` before saving configuration and lock state.

Three constraints shape this design:

- `PackManifest.PackManagedFile.Source` currently means a pack-relative file path, while the requested external-source syntax uses `source` for a pack-local alias and `path` for the selected file.
- `ConfiguredSourceIdentity` currently preserves mostly literal URL and ref values, so it cannot establish transport-independent source equality.
- ADR-0047 currently permits removal of a source consumed by installed lock state, while this change requires consumer-aware refusal.

The detailed behavior contracts are in the seven delta specs under [specs](specs).

## Goals / Non-Goals

**Goals:**

- Add one canonical source-identity service used by schemas, configuration commands, links, lifecycle planning, locking, caching, and audit.
- Keep pack-local aliases separate from authoritative workspace identifiers throughout the resolved graph.
- Resolve all source requirements and managed targets before mutating the workspace.
- Preserve existing manifests, workspace source behavior, ownership rules, script trust boundaries, and Native AOT compatibility.
- Reuse existing Git execution, filesystem abstraction, globbing, state-store, and rollback mechanisms.

**Non-Goals:**

- Introduce a second catalog or provider type for external content.
- Permit pack-defined local sources, external scripts, direct downloads, credentials in manifests, automatic source cleanup, or non-interactive identifier remapping.
- Make the cache authoritative or persist pack consumer metadata in `lunapack.yml`.
- Replace existing managed-file strategies, target remapping, template rendering, or local-modification protections.

## Decisions

### 1. Normalize declarations into a source fingerprint value object

Add a `SourceFingerprint` value object and a single normalization service consumed by configuration validation, source commands, link commands, requirement planning, cache keys, lock drift checks, and output formatting. Keep transport data needed to access a repository on the source declaration; use only sanitized repository identity, canonical ref, and normalized base path in equality and fingerprint serialization.

Git URL normalization will parse supported HTTPS, `ssh://`, and scp-style forms structurally. It will reject user information in persisted declarations, lowercase hosts, remove optional `.git` and trailing separators, and lowercase owner/repository only for `github.com`. Unknown providers retain path case. Local fingerprints will use `CanonicalProjectPath` and remain type-distinct. Repository-relative paths will pass through `ProjectPath` before segment validation, with `/` representing the root.

Ref canonicalization will query branch and tag namespaces explicitly. Exact commits remain lowercase commit identities. A short ref matching exactly one namespace becomes its complete ref; zero or multiple matches fail. Workspace sources that omit a ref retain remote-HEAD resolution, but matching a pack requirement uses the resulting unambiguous complete ref for that operation.

Alternatives considered:

- Reuse `ConfiguredSourceIdentity` record equality. Rejected because URL transport, case, short refs, and equivalent paths remain unequal.
- Fingerprint the resolved commit. Rejected because branch and commit declarations have intentionally different update semantics.
- Normalize every provider path to lowercase. Rejected because repository paths can be case-sensitive outside GitHub.

### 2. Add a compatibility adapter for managed-file selectors

Keep `managedFiles` as the top-level pack collection. Extend its schema with `path`, external alias `source`, repeatable `exclude`, and `flatten`. Normalize every entry into an internal selector containing selector kind, selector value, optional source alias, exclusions, and flattening before validation or planning.

Existing `{ source: <pack-relative-file>, target: ... }` entries remain valid and are interpreted as legacy direct-file selectors when no `path`, `directory`, or `glob` selector is present. New direct files use `{ path: <pack-relative-file>, target: ... }`; new external files use `{ source: <alias>, path: <source-relative-file>, target: ... }`. For directory and glob entries, optional `source` is always an alias. Authoring commands write the new canonical shape, while reading and lifecycle operations continue to accept the legacy shape.

This disambiguation avoids an immediate manifest schema-version break. Serialization after an authoring mutation may canonicalize a legacy file selector from `source` to `path` without changing its meaning.

Alternatives considered:

- Rename the alias property to `sourceId`. Rejected because it diverges from the requested pack contract.
- Interpret every `source` value as an alias. Rejected because it breaks all existing file-only manifests.
- Add a new top-level `files` collection. Rejected because it duplicates the established `managedFiles` contract and lifecycle pipeline.

### 3. Treat pack source requirements as graph planning inputs

Add an external-source requirement planner between composite graph resolution and managed-file planning. It will:

1. Scan selected managed-file entries in every resolved pack.
2. Resolve each referenced pack-local declaration to a canonical fingerprint.
3. Ignore declarations with zero selected file references and retain warnings for authoring validation.
4. Group requirements by fingerprint.
5. Match groups to the normalized workspace source index.
6. Assign existing workspace identifiers as authoritative mappings.
7. Propose deterministic identifiers for missing groups.
8. Resolve interactive identifier conflicts before the combined approval prompt.
9. Produce an immutable source mapping and candidate `ProjectConfiguration` for downstream planning.

Proposed identifiers use the root pack's alias when available; otherwise they use the lexically first `(pack ID, alias)` pair. A proposed identifier already assigned to another missing group is treated like a workspace conflict. Pack descriptions remain approval metadata and do not participate in fingerprints or workspace identity.

The planner may perform non-interactive Git metadata lookup before approval to canonicalize refs, but it will not materialize repository content. After approval, an external-source materializer resolves commits and content roots through the existing `GitProcessRunner` safety boundary. No approval path authorizes lifecycle scripts or accepts credential prompts.

Alternatives considered:

- Resolve sources while visiting each pack. Rejected because it creates repeated prompts, alias-order dependence, and partial graph decisions.
- Persist alias mappings in `lunapack.yml`. Rejected because mappings are installation evidence and would couple portable configuration to pack consumers.
- Use the pack alias even when an equivalent workspace source exists. Rejected because workspace identifiers are the stable authority.

### 4. Generalize managed-file planning around content roots

Extend each resolved pack with a lookup from pack-local source alias to a materialized external content root and provenance record. `PackInstallationPlanner` will resolve every normalized selector against either the pack directory or its mapped external root, then feed resulting files into the existing target remapping, condition, template, strategy, ownership, and conflict pipeline.

Directory traversal and glob expansion will produce deterministic ordinal path order. Exclusions run against normalized source-relative paths after primary matching. Target suffixes derive from the non-wildcard selection root; flattening replaces each suffix with its basename and rejects duplicates before filesystem checks. Every selected path and followed symbolic link must remain below the materialized root. Target paths continue through `ProjectPath.NormalizeProjectRelativePath` and existing ownership preflight.

Alternatives considered:

- Copy external content into each pack directory before planning. Rejected because it obscures provenance and duplicates cache content.
- Give external sources ownership. Rejected because ownership belongs to the pack manifest that declares target behavior.

### 5. Extend the existing transaction instead of adding a second commit protocol

Approved additions exist only in a candidate `ProjectConfiguration` during preparation. Git resolution, selector expansion, target conflict checks, hook authorization, and complete lock construction all finish before `PackUpdateTransaction.Apply` mutates files. `ProjectStateStore.SaveAsync` then persists candidate configuration and lock state. Any state-save failure invokes the existing managed-file rollback; source additions therefore disappear with the candidate state.

Source rename uses the state store's two-document replacement and updates trust and every lock/link source reference in memory first. Source removal checks lock consumers before building candidate state. The implementation will replace ADR-0047's unavailable-source exception with consumer-aware refusal and will retain narrow compatibility reads for historical lock state.

Alternatives considered:

- Save approved sources before materialization. Rejected because later Git or target failures would leave partial configuration.
- Create a separate transaction manager for source additions. Rejected because the current file rollback plus atomic state-save boundary already spans the required mutation set.

### 6. Make external provenance conditional and backward compatible

Extend `ProjectLockFile.ResolvedPack` with an alias-keyed external-source mapping. Extend `ManagedFile` with nullable external provenance fields that become collectively required when the file came from an external source. Pack-sourced files retain the current shape. Lock validation will reject partial external records and cross-check their source identifier and fingerprint against the pack mapping.

Successful lifecycle writes use the current lock shape. Existing lock files without external fields remain valid and mean that managed content came from the owning pack. No eager migration occurs. Source rename updates authoritative identifiers but not fingerprints or pack aliases.

Alternatives considered:

- Store full source declarations on every file. Rejected because it duplicates data and increases drift risk.
- Store only fingerprints. Rejected because audit and rename need human-readable authoritative identifiers and pack aliases.

### 7. Separate external-content freshness from pack-version selection

Update preparation will resolve current external symbolic refs even when the selected pack version is unchanged. It will compare the newly selected path set and content hashes against lock state, not commits alone. A moved ref with unchanged selected content is current; changed content or glob membership yields an update plan. Removed requirements delete lock consumers but do not mutate source configuration.

`outdated` reuses the same comparison in read-only mode. `--offline` forbids remote resolution and reports uncertainty while using cached commits and locked hashes. Drift detection compares locked fingerprints to current configured fingerprints before source resolution and blocks update pending an explicit future acceptance workflow.

Alternatives considered:

- Mark every moved ref outdated. Rejected because unrelated upstream commits would create noisy updates.
- Check only content hashes and ignore selected paths. Rejected because glob additions and removals are observable pack changes.

### 8. Keep command registration thin and consent interfaces injectable

Add source approval and identifier-prompt interfaces alongside existing lifecycle confirmation abstractions. Interactive implementations use `CliConsole`; deny implementations support redirected input and tests. Add `--accept-sources` to install and update request models, not to shared script modes. Keep `rm` as the existing canonical removal verb and accept `remove` as a compatibility alias for the requested source authoring and workspace examples.

GitHub shorthand is command parsing only. Both workspace and pack commands produce normal Git declarations before validation and persistence. Output formatters receive sanitized normalized identities and never raw credential-bearing input.

Alternatives considered:

- Reuse script trust confirmation. Rejected because source retrieval and script execution are separate consent decisions.
- Introduce a `github` source model. Rejected because hosting shorthand must not become a persisted source type.

### 9. Record durable decisions and documentation by audience

Create ADR-0051 for normalized fingerprint authority, pack-alias mapping, graph-wide approval, and consumer-aware source removal. Mark ADR-0047 as superseded by ADR-0051 without rewriting its historical decision; ADR-0051 will also explain why `remove` is an accepted alias while `rm` remains canonical under ADR-0048.

Update product requirements with accepted external-source behavior, internal architecture with resolver and transaction boundaries, internal security guidance with consent and path controls, and developer documentation for schema syntax and author/consumer workflows. Do not present the behavior as available until implementation ships.

## Risks / Trade-offs

- [Remote ref canonicalization can add latency before approval] -> Cache ref metadata, enforce existing timeouts and cancellation, and perform one lookup per normalized repository/ref candidate.
- [Equivalent URLs can be parsed incorrectly] -> Use structured URI/scp parsing, provider-specific normalization tests, credential redaction tests, and conservative rejection for unsupported forms.
- [Legacy `source` selectors are context-sensitive] -> Centralize compatibility parsing, expose one normalized selector model, and cover mixed old/new manifests in schema and store tests.
- [Cross-volume or interrupted writes cannot be perfectly atomic] -> Stage state files beside their destinations, snapshot managed files, restore on failure, and retain explicit best-effort rollback diagnostics.
- [Graph-wide resolution increases memory and cache use] -> Materialize one root per fingerprint/commit and share it across pack aliases for the operation.
- [Symlinks can escape a materialized root] -> Resolve canonical paths before reads and reject any path outside the approved root.
- [Source rename touches trust and link consumers] -> Build and validate the complete candidate state before replacing files; reject unknown reference shapes.
- [Changing ADR-0047 restricts an existing command] -> Treat consumed-source removal as an intentional safety change, document the migration, and preserve uninstall as the supported way to release consumers.

## Migration Plan

1. Add compatibility schema and model changes so existing manifests and lock files still load before behavior is enabled.
2. Introduce and test source normalization, fingerprints, canonical refs, and uniqueness validation; route existing source and cache identity comparisons through them.
3. Add pack authoring and external selector parsing while retaining legacy `source`-path reads.
4. Add graph requirement planning, consent, materialization, and managed-file planning behind the complete preflight boundary.
5. Extend lock writing, audit, outdated, update, rename, removal, dry-run, and guidance behavior.
6. Add ADR-0051, mark ADR-0047 superseded, and update internal, product, and developer documentation.
7. Validate schemas, focused unit and integration tests, full CLI tests, formatting, and Native AOT publish.

Rollback consists of reverting the implementation before any externally sourced lock state is accepted. After release, rollback must retain readers for the additive lock fields or require users to uninstall affected packs first; silently discarding external provenance is not acceptable.
