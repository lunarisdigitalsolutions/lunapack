## Context

See [proposal.md](proposal.md) for motivation. The current project manifest
mixes configured sources with resolved installed-pack paths and managed-file
digests. Pack discovery already centralizes local-source catalog resolution,
but installation resolves one pack at a time and persists state through
`ProjectManifest`. The current schema version is `1` and permits
machine-specific paths in resolved pack records.

This is a breaking configuration-model change. It must preserve the existing
catalog/provider boundary, source ordering, Semantic Version precedence, and
safe complete-file lifecycle rules described in the existing specifications.

## Goals / Non-Goals

**Goals:**

- Separate portable desired configuration from resolved lifecycle state.
- Allow a pack to compose exact versions of zero or more packs, including a
  contentless composite pack.
- Resolve the complete graph from consumer-configured sources before mutation.
- Persist portable source provenance, graph edges, ownership, and digests for
  future synchronization and reconciliation.

**Non-Goals:**

- Creating the Azure/.NET/Angular example composite pack.
- Adding remote source providers, version ranges for composite references,
  content merges, target-directory creation, or a user-facing sync command.
- Supporting multiple resolved versions of one pack ID in a project graph.
- Automatically relocating legacy source directories that cannot be expressed
  relative to the project.
- Providing a migration path, CLI command, or compatibility behavior for the
  previous combined-state configuration.

## Decisions

### Document boundary without a configuration version increment

`lunapack.yml` remains schema version `1` but adopts portable desired state:

```yaml
schemaVersion: 1
sources:
  - type: local
    path: projects/packs
packs:
  - id: platform-composite
    version: 1.0.0 # optional only when the root request is explicitly pinned
```

`lunapack-lock.yml` becomes version `1` resolved state. It stores each selected
pack once, its exact version, a reference to the configured relative source,
the pack's path relative to that source, dependency edges, and managed
target/digest records. All persisted filesystem paths are project-relative or
source-relative; CLI code may calculate absolute paths only while accessing the
filesystem.

This retains a readable declarative root-packs list in configuration while
placing reproducibility and lifecycle ownership in a generated lock document.
Keeping the old single document would retain machine-specific data and makes
transitive ownership ambiguous. Storing configured source declarations in pack
manifests would make a reusable pack choose its consumer's trust boundary, so
it is rejected.

The prior version-1 document shape is intentionally unsupported even though
the configuration schema version remains `1`. Documents containing resolved
source provenance, managed-file ownership, or digests fail validation. Users
must recreate project state; LunaPack provides no migration command or automatic
conversion in this change.

### Composite manifest shape and graph invariants

Extend `pack.yml` with an optional `packs` collection. Each element contains
an ID and exact Semantic Version. `managedFiles` becomes optional, but schema
validation requires at least one managed file or composite reference. A
composite is therefore an ordinary pack, not a separate pack type, and can
contribute both references and files.

Build a depth-first resolver over `id@version` nodes. It resolves direct root
requests with existing latest/explicit-version behavior, but resolves composite
edges only at their declared exact version. It uses the configured source
catalog and existing source-order tie-breaker for every node. Maintain a
visiting set for cycle detection, a resolved-by-ID map for single-version
enforcement, and a target ownership map for conflict detection. A missing
node, cycle, version conflict, duplicate target, unavailable template, or
unowned existing target fails the whole plan before mutation.

Allowing floating or range-based composite references was rejected because it
would make an authored composite non-reproducible. Allowing multiple versions
of one ID was rejected because complete-file target ownership and later sync
semantics would be unclear.

### Lifecycle transaction and ownership

Installation first resolves and validates the whole graph, builds its complete
target plan, and derives the next configuration and lock documents in memory.
It stages document writes and file copies so persistence failure can roll back
all new targets and preserve prior documents. Only root user requests are
written to `lunapack.yml`; every reachable node is recorded in the lock file.

Uninstallation accepts only a configured root request. Compute the remaining
reachable graph from the other roots, then remove only nodes no longer
reachable. Preflight all affected managed files against their lock digests.
Stage removable files for restoration until the updated documents commit; a
failed operation restores both the prior files and documents. Shared
dependencies and their files remain owned until no root reaches them.

This is more complex than treating every installed pack as independent, but it
is necessary to avoid deleting a dependency still used by another root.

### Source path validation

Validate local source values as relative paths both in schema validation and at
the `source add local` command boundary. The command resolves a supplied
relative path against the project only to verify directory existence, then
persists the original normalized relative form. It rejects rooted drive, UNC,
and filesystem-root paths before writing configuration.

The former combined-state document is not a valid portable configuration.
Supporting it through dual behavior or migration would retain two ownership
authorities and prolong non-portable state, so both are excluded.

### Documentation and decision record

Create ADR-0016 from the repository template to establish the durable split
between configuration and lock state, relative-source portability, and
consumer-owned source selection for composite packs. Update the ADR index.
Update product requirements to distinguish configuration intent from resolved
state; update internal pack, source-provider, dependency, lifecycle, and trust
architecture guidance; and update developer configuration, schema, manifest,
dependency, install, uninstall, audit, and future-sync guidance. Document the
Azure/.NET/Angular composite only as an illustrative composition, not an
implemented pack.

## Risks / Trade-offs

- [Two-document state can become inconsistent after interruption] -> Use
  temporary files, staged target backups, and recoverable replace/rollback
  behavior; test every failed-write boundary.
- [Existing projects use the former combined-state document] -> Reject it as
  invalid and require users to recreate portable configuration and lock state.
- [Composite graph bugs could delete shared content] -> Compute reachability
  before deletion and test shared, unshared, cyclic, and modified-target cases.
- [Large source catalogs increase recursive resolution work] -> Reuse the
  catalog per operation and defer caching until measurements justify it.
- [User-edited lock files can invalidate ownership] -> Validate schema and
  graph consistency before every lifecycle operation and fail safely.

## Implementation Plan

1. Add schema-version-1 portable configuration, version-1 lock, and composite-manifest schema
   models plus focused schema fixtures.
2. Refactor stores so configuration and lock state load, validate, stage, and
   persist as a coordinated operation.
3. Add graph resolution and preflight lifecycle planning, then implement
   install/uninstall transaction paths with rollback.
4. Rewrite the repository's root configuration as portable state and commit the
   resulting `lunapack-lock.yml`.
5. Update documentation and ADR-0016, then run schema, unit, integration, and
   full CLI quality validation.

Transactional lifecycle rollback restores the previous configuration, lock
file, and managed targets after a failed operation. Releases do not support
the former combined-state document.
