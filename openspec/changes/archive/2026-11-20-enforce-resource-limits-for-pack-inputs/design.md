# Design

## Context

See `proposal.md` for motivation and `specs/pack-resource-limits/spec.md` for
the behavior contract. Current OCI archive processing already bounds entry
count, compressed bytes, per-entry bytes, and expanded bytes. Project state,
pack manifests, and user settings are also read in several places before
deserialization; graph resolution, pack selection, and template rendering can
materialize collections or strings before a limit is checked.

The repository contains 53 pack versions. The largest checked-in `pack.yml` is
2,581 bytes; the largest pack contains 13 files and 11,007 total bytes, and its
largest individual file is 8,684 bytes. Proposed pack-manifest, document, and
file-count defaults leave substantial headroom. No checked-in consumer
`lunapack.yml` or lock-file samples provide a comparable production baseline.

## Goals / Non-Goals

**Goals:**

- Resolve one typed set of effective limits for each operation.
- Bound bytes and counts before parsing, collection growth, rendering, or
  mutation.
- Preserve existing archive protections and transactional lifecycle behavior.
- Let project configuration lower defaults; only user-owned scopes may raise
  them.

**Non-Goals:**

- Change pack or lock schema versions.
- Change script trust, script sandboxing, network policy, or pack selection
  semantics.
- Add environment-variable or command-line flag overrides separate from
  `luna config`.

## Decisions

### Extend Luna's existing scoped configuration

Add typed numeric resource-limit settings to the existing `luna config`
surface. Keep current Boolean preferences and their serialization behavior
unchanged. Use the existing scope stores and precedence: project, then
user-project-specific, then global-user, then built-in defaults for Boolean
preferences. Resource limits use user-project before global-user, then apply a
project value only as a lower cap. `set`, `list`, and `rm` use the existing
command and scope behavior.

Project-controlled values may lower, but never raise, built-in defaults.
Global-user and user-project-specific settings may raise or lower defaults.
Every value must be positive and within a finite hard ceiling; no
zero/unlimited sentinel is accepted. User-owned ceilings are 16 MiB for project
configuration, 64 MiB for pack manifests, 1 GiB for lock files, 256 MiB per
pack document, 16 GiB per pack snapshot, 256 MiB per rendered file, 4 GiB per
lifecycle operation, graph depth 128, 100,000 unique nodes, 1,000,000
references, 100,000 files per pack, 1,000,000 files per graph, 1,000,000 OCI
entries, 16 GiB OCI download and expansion, and 4 GiB per OCI entry. A compiled
bootstrap ceiling protects project and user settings before their own limit
values can be read: 16 MiB for project configuration and 1 MiB for
`~/.lunapack/config.yml`. A document cannot raise its own bootstrap ceiling.

This reuses the established scope contract instead of adding another
configuration file or independent CLI override mechanism. It also keeps a
repository-controlled project from raising the default resource ceiling for
other users.

### Enforce limits at producer boundaries

Resolve limits once, then pass the effective policy through project-state
loading, pack loading, source materialization, graph resolution, file
selection, rendering, and OCI archive handling. Use byte-counting reads and
copies; metadata lengths may reject early but are not the sole enforcement.
Use checked arithmetic and reject before parsing or retaining data beyond the
limit.

Count files while walking pack snapshots and while expanding selectors, before
filtering or materializing full result lists. Track a shared operation budget
across all requested roots. Keep cycle detection separate from resource
accounting: count unique graph nodes, reference traversals, and root-to-node
depth so duplicate edges or repeated paths cannot evade the work limit.

For templates and lifecycle instruction documents, bound source reads before
rendering and bound UTF-8 output while it is produced. Track cumulative
rendered bytes in the plan as well as the per-file output. Reuse the existing
bounded-copy approach used by OCI archive processing.

### Keep configuration errors and limit failures explicit

Validate settings when they are loaded and when `luna config` mutates them.
Report the invalid key and allowed range instead of silently falling back.
Limit diagnostics identify the operation, observed count or byte lower bound,
effective limit, and unit, but never include pack content or credentials.
Perform all checks during preparation, before hook authorization and workspace
mutation, preserving existing rollback and state-ownership guarantees.

### Preserve compatibility and existing archive behavior

Absent resource-limit settings use defaults; existing project and user
configuration remains readable without migration. Keep existing OCI default
values and path, digest, duplicate-entry, and link validation. Apply overrides
to the existing archive limits only through the same effective configuration.
No independent archive behavior or schema version changes are introduced.

### Record durable policy

Update public guidance with defaults, setting names, units, scope examples, and
the project-scope restriction. Update internal threat-model and architecture
guidance with boundary placement and the rationale for user-owned increases.
Record the compatibility and trust boundary in a new ADR, and add the
externally observable rejection behavior to the CLI changelog.

## Risks / Trade-offs

- **Risk:** User-owned overrides can increase resource consumption.
  **Mitigation:** Require explicit user configuration, reject non-positive or
  over-ceiling values, retain bootstrap ceilings, and document the impact.
- **Risk:** New defaults can reject unusually large existing packs.
  **Mitigation:** Base defaults on repository pack measurements, preserve
  user-owned scoped overrides, and report exact values and units.
- **Risk:** Limits checked only in the planner would not bound parsing,
  enumeration, or rendering allocations.
  **Mitigation:** Enforce byte and count budgets at the read, traversal,
  enumeration, and output producers.
- **Risk:** Resource-limit diagnostics could leak untrusted content.
  **Mitigation:** Report only input category, observed size/count, limit, and
  unit.

## Migration Plan

Add optional typed values to existing Luna configuration stores. Keep absent
values equivalent to the built-in defaults and preserve all existing
configuration values. No migration or schema-version bump is required.

## Open Questions

None.
