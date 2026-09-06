## Context

See [proposal.md](proposal.md) for motivation. Current project configuration and lifecycle services use pack ID as requested-root identity, while lock records combine resolution metadata and managed-file ownership in one pack record. That model cannot represent sibling installations or update one without selecting and preserving the others.

The change crosses CLI parsing, project configuration, graph resolution, lifecycle planning, ownership, schema validation, serialization, and documentation. Existing version-1 project and lock files must remain readable. Pack manifests remain unchanged.

## Goals / Non-Goals

**Goals:**

- Give each requested root a stable `(pack ID, alias)` identity.
- Isolate root inputs, effective placement, owned files, and lifecycle mutation by instance.
- Reuse exact matching transitive resolutions without coupling sibling root lifecycles.
- Preserve atomic preflight, mutation, rollback, drift protection, and unique target ownership.
- Upgrade existing state without requiring users to edit generated lock files.

**Non-Goals:**

- References from one installed instance to another.
- Instance-scoped dependency declarations in `pack.yml`.
- Shared values or automatic synchronization between sibling instances.
- Automatic relocation during rename.
- Interactive target-conflict resolution.

## Decisions

### Use pack ID plus alias as requested-root identity

Introduce one value type for requested-root identity containing pack ID and effective alias. An omitted configuration alias is normalized in memory to the pack ID. Equality is exact and case-sensitive, matching existing pack identity handling. Alias uniqueness is enforced within a pack ID, not globally.

All lifecycle APIs that currently accept or return a requested pack must carry this identity. Internal dictionaries and joins must stop keying requested roots by pack ID alone. Pack ID remains catalog identity; alias never affects pack discovery or trust.

Alternative: use alias as a repository-wide identity. Rejected because commands already include pack ID and the chosen contract permits aliases to repeat across different packs.

### Keep portable instance intent in configuration and installed truth in lock state

Add optional `name` to each `lunapack.yml` requested pack entry. Existing `version`, `destination`, and `remap` fields remain on that entry and therefore become instance-scoped. Omission means the default alias, preserving existing files and hand-authored configuration.

Advance `lunapack-lock.yml` to schema version 2. Add explicit requested-root instance records containing pack ID, alias, exact root resolution key, canonical placement evidence, and directly owned managed files. Keep resolved pack nodes separate and key them by pack ID, exact version, source identity, and immutable source revision where applicable. Dependency edges point to resolved-node keys, not aliases. Exact matching nodes may be shared by multiple instances; differing versions or source states remain distinct nodes.

Shared transitive managed files remain owned by their resolved pack node and are retained while any instance reaches that node. Direct root files are owned by the instance. Every effective target still has one owner across instances, shared nodes, and links.

Alternative: duplicate a complete resolved graph inside every instance. Rejected because it duplicates provenance, complicates reachability, and contradicts shared transitive state.

Alternative: add an alias directly to the existing resolved pack record. Rejected because one resolved release may be reached by several roots and because pack-ID references become ambiguous when root records repeat.

### Compare canonical effective placement, not raw option spelling

After selecting the candidate release, resolve `--destination`, project remaps, instance remaps, command remaps, and path normalization through the existing target resolver. Build a sorted mapping of each directly declared target identity to its effective target. An additional instance is distinct only when at least one resulting pair differs from every existing instance of the same pack.

This makes a destination count when it changes targets and prevents an explicit identity remap from bypassing duplicate detection. A contentless pack cannot have a second instance because it has no observable target placement to distinguish.

Alternative: compare raw remap dictionaries. Rejected because semantically equivalent paths and identity mappings would permit accidental duplicates.

### Resolve and mutate each instance independently

Install, named update, update-all, outdated, audit, and uninstall construct one root plan per instance. Parameter and variable binding starts from clean instance-local resolution state for each plan. Sibling results are never reused as input.

An update changes only the selected instance's requested version, resolution edge, direct ownership, and newly reachable or unreachable shared nodes. Siblings can therefore remain on another root version. The existing transaction coordinator validates the union of retained state and the candidate plan before mutation.

Target checks distinguish three states:

- Target is owned by selected instance: apply existing update strategy and drift rules.
- Target is owned by another instance, shared pack, or link: fail preflight.
- Target is unowned: overwrite without prompting, then record ownership and installed digest.

Alternative: make all sibling instances advance together. Rejected because it defeats independently selectable updates and creates hidden cross-instance coupling.

### Select omitted aliases deterministically

For update and uninstall with no `--name`, select the only matching instance when one exists. With several matches, select the pack-ID-named default when present. Otherwise fail before resolution, list aliases, and emit complete commands using `--name`.

`--name` is valid only for a single pack reference. Update-all and outdated enumerate `(pack ID, alias)` identities and report both. This preserves concise commands for existing repositories while making ambiguity explicit.

### Rename identity transactionally

`luna rename <pack-id> --name <alias> --to <new-alias>` validates the source and destination identities, then updates configuration, instance lock records, and nested ownership references in one project-state transaction. It performs no catalog resolution, file write, hook, or parameter evaluation. A failed state write restores both documents.

Alternative: implement rename as uninstall plus install. Rejected because that can lose local changes, rerun hooks, and alter generated bytes.

### Ignore dependency back-edges deterministically

Track the active recursion path during each instance's depth-first graph resolution. Ignore self-edges immediately. When an edge points to a node already on the active path, emit one warning containing the ordered cycle and omit that edge. Continue traversal of other edges. Completed nodes may still be reused through non-cyclic edges.

The resolver still rejects incompatible versions within one instance graph and validates target ownership across the combined lifecycle plan. Traversal follows existing manifest reference order, making the ignored edge and warning deterministic.

Alternative: collapse strongly connected components. Rejected because pack references do not define merge semantics and a back-edge omission is smaller, deterministic, and matches the requested resilient behavior.

### Treat schemas and documentation as contract changes

Extend the version-1 `lunapack.yml` schema with optional requested-pack `name`; this is backward compatible because omitted names retain default semantics. Publish lock schema version 2 for the structural split between instances and resolved nodes. Runtime readers accept supported version-1 state and version-2 state; writers emit version 2 after successful lifecycle mutation.

Create an ADR for instance identity, lock separation, dependency sharing, cycle handling, and overwrite policy. Update product lifecycle concepts, internal runtime and lock architecture, and developer CLI/configuration/lock/remap/ownership guidance in the same implementation change.

## Risks / Trade-offs

- [Schema-v2 migration loses ownership correlation] -> Derive instances only from validated configuration roots and exact version-1 records; abort without mutation when correlation is ambiguous.
- [Independent instances render colliding targets] -> Compare complete retained and candidate ownership before any write; never transfer ownership implicitly.
- [Unowned overwrite destroys local content] -> Show overwrite actions in normal and dry-run plans, preserve transaction backups for rollback, and document this intentional breaking behavior.
- [Cycle warnings hide author mistakes] -> Include the full ordered cycle and ignored edge, keep deterministic traversal, and retain validation for version and target conflicts.
- [Shared dependency cleanup removes needed files] -> Compute reachability from every retained instance against exact resolved-node keys before deleting shared state.
- [Pack-ID-only assumptions survive in secondary commands] -> Cover install, update, update-all, outdated, audit, uninstall, guidance, trust display, and state validation with identity-focused tests.

## Migration Plan

1. Add backward-compatible `name` support to project configuration and introduce lock schema version 2 models and validation.
2. Read version-1 locks into an in-memory default instance for each unaliased requested root. Do not rewrite state during read-only commands.
3. Route lifecycle selection, planning, ownership, graph reachability, and output through instance identity.
4. On the first successful mutating lifecycle command, atomically write instance-aware configuration and version-2 lock state with unchanged files, provenance, strategies, and digests preserved.
5. If migration or schema validation fails, restore project files and both state documents and keep the version-1 lock usable by the prior CLI release.

Rollback before a version-2 write uses the previous executable and unchanged version-1 files. After a version-2 write, rollback requires restoring the transaction backup or source-control revision because older releases do not understand schema version 2.
