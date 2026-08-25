## 1. Configuration and Schema Contracts

- [x] 1.1 Split the current project-manifest domain model and persistence store into schema-version-1 portable configuration and version-1 lock-state models, including coordinated load, validation, staging, and rollback support.
- [x] 1.2 Update `pack.schema.json` to support exact-version composite `packs` references, permit file-only, composite-only, and mixed packs, and reject source declarations and empty packs.
- [x] 1.3 Update `lunapack.schema.json` for schema-version-1 relative local sources and requested root packs; add a versioned `lunapack-lock.yml` JSON Schema for resolved graph, portable provenance, dependencies, managed targets, and digests.
- [x] 1.4 Add schema fixtures and validation tests for contentless composites, mixed composites, unpinned references, prohibited sources, absolute paths, separated lock state, and unsupported document versions.

## 2. Project State

- [x] 2.1 Update `lunapack init` and project-state validation so new projects receive empty schema-valid schema-version-1 `lunapack.yml` and `lunapack-lock.yml` documents, and existing state is never overwritten.
- [x] 2.2 Update `lunapack source add local` to reject rooted paths, verify supplied relative paths against the project directory, normalize persisted relative paths, and preserve duplicate/unavailable-source safeguards.
- [x] 2.3 Rewrite the repository root configuration as schema-version-1 portable state and generate its committed `lunapack-lock.yml` without retaining absolute source paths.

## 3. Composite Resolution and Lifecycle

- [x] 3.1 Implement depth-first composite graph resolution over exact `id@version` references using only consumer-configured sources and existing source-order tie breaking.
- [x] 3.2 Add graph preflight validation for missing references, cycles, conflicting versions for one ID, duplicate target ownership, unavailable templates, and unowned existing targets.
- [x] 3.3 Refactor installation to add only the direct request to `lunapack.yml`, record every resolved node and managed digest in `lunapack-lock.yml`, and commit files plus both documents with transactional rollback.
- [x] 3.4 Refactor uninstallation to accept requested roots only, calculate remaining graph reachability, preserve shared dependencies, protect modified targets by lock digest, and restore staged removals if persistence fails.
- [x] 3.5 Update audit and related lifecycle reporting to read resolved provenance, ownership, and transitive relationships from `lunapack-lock.yml`.

## 4. Test Coverage

- [x] 4.1 Add focused unit tests for configuration/lock serialization, relative-path validation, graph traversal, source precedence, cycles, missing nodes, version conflicts, and target conflicts.
- [x] 4.2 Extend CLI integration tests for initialization, relative source registration, direct installs, contentless and mixed composite installs, shared/unshared dependency removal, modified targets, and atomic failure rollback.
- [x] 4.3 Add schema and integration fixtures that model the Azure Bicep, GitHub Actions, ASP.NET Core, and Angular composition as references only, without publishing an example composite pack.

## 5. Documentation and Governance

- [x] 5.1 Create ADR-0016 from the ADR template and index it, recording portable configuration versus lock state, relative-source policy, and consumer-owned source selection for composite packs.
- [x] 5.2 Update product requirements and internal architecture/governance guidance for the new configuration boundary, pack graph, provider provenance, lifecycle ownership, dependency policy, trust, breaking compatibility, and sync foundation.
- [x] 5.3 Update developer configuration, manifests, schemas, dependencies, pack authoring, and install/uninstall/audit command documentation with schema-version-1 configuration, lock-file semantics, breaking-change guidance, and composite-pack examples.

## 6. Verification

- [x] 6.1 Run schema validation, focused unit and integration test suites, CSharpier formatting, analyzers, and the full CLI quality build; verify repository state contains only relative persisted source paths.
