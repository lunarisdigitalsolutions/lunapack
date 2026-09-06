## 1. Instance Identity and State Contracts

- [x] 1.1 Add a requested-root instance identity value using pack ID plus effective alias, normalize omitted aliases to pack ID, and verify unit tests cover exact case-sensitive equality and per-pack alias scope.
- [x] 1.2 Extend requested-pack configuration models and `projects/schema/lunapack.schema.json` with optional `name`, validate unique effective aliases per pack, and verify schema and project-state tests accept legacy unnamed roots plus repeated IDs with distinct aliases.
- [x] 1.3 Introduce lock schema version 2 with explicit instance records, exact resolved-node keys, canonical placement evidence, and unambiguous file ownership; verify serialization and JSON Schema tests cover local, Git, external-source, link, and multi-instance state.
- [x] 1.4 Add version-1 lock migration that derives default instances without rewriting on read and writes version 2 only after successful mutation; verify migration tests preserve provenance, strategies, declared/effective targets, digests, and reject ambiguous roots without mutation.

## 2. Dependency and Placement Planning

- [x] 2.1 Refactor graph resolution to plan each requested-root instance independently and share only exact matching pack/version/source resolution nodes; verify tests cover sibling root versions, exact-node reuse, distinct transitive nodes, and within-instance version conflicts.
- [x] 2.2 Replace cycle failure with deterministic self-edge and active-path back-edge suppression, including ordered warnings; verify direct, transitive, and non-cyclic shared-edge resolver tests pass.
- [x] 2.3 Build canonical declared-to-effective target mappings through existing destination, remap precedence, and path normalization; verify tests distinguish changed destinations/remaps and reject identity mappings, equal placements, and duplicate contentless-pack instances.
- [x] 2.4 Make parameter, variable, condition, template, and external-source resolution state instance-local; verify sibling instances render different values without propagation and retain independent resolution evidence.
- [x] 2.5 Update ownership preflight to allow overwrite of unowned targets while rejecting targets owned by another instance, shared pack, or link; verify dry-run, mutation, rollback, drift, and cross-owner conflict tests.
- [x] 2.6 Extract copy, merge, lines, section, and JSON update planning into focused classes while preserving existing action behavior; verify all `PackUpdatePlannerTests` pass.

## 3. Instance Lifecycle Commands

- [x] 3.1 Add `--name` to single-pack install parsing and persist named/default instance identity with instance-scoped destination and remaps; verify command tests reject duplicate aliases, multiple references with `--name`, and indistinguishable placements.
- [x] 3.2 Implement deterministic instance selection for update and uninstall: sole instance, then default alias, otherwise explicit ambiguity; verify command tests cover each branch and unknown aliases.
- [ ] 3.3 Update named, update-all, outdated, and audit flows to key plans and results by pack ID plus alias without mutating siblings; verify sibling versions and ownership remain independent across focused lifecycle tests.
- [ ] 3.4 Scope uninstall and shared-node reachability to the selected instance; verify removing one sibling preserves its peers and dependencies still reachable from any instance.
- [ ] 3.5 Add `luna rename <pack-id> --name <alias> --to <new-alias>` as an atomic state-only mutation; verify success preserves files and digests while invalid, duplicate, and failed-write cases leave both state files unchanged.
- [ ] 3.6 Update contextual output and next-step guidance to display aliases and complete disambiguating `--name` commands; verify guidance tests stay within the existing action limit and never suggest sibling cleanup.

## 4. Documentation and Architecture Records

- [ ] 4.1 Create the next ADR from `docs/internal/architecture/adr/template.md` for instance identity, lock-v2 separation, shared resolution nodes, cycle-edge suppression, and unowned overwrite policy; add it to the ADR index and verify Markdown links.
- [ ] 4.2 Update `docs/product/pack-ecosystem-working-concept.md` and relevant lifecycle PRDs with accepted multi-instance scope, defaults, non-goals, and breaking safety changes; verify product documents do not describe implementation details.
- [ ] 4.3 Update internal runtime, ownership, dependency-resolution, and lock-state documentation for instance-aware invariants and migration; verify internal guidance matches schema version 2 and transaction behavior.
- [ ] 4.4 Update developer install, update, remap, CLI command, configuration, lock-file, ownership, and troubleshooting documentation with named examples and ambiguity recovery; verify public guidance marks behavior available only with the implementing release.
- [ ] 4.5 Add externally observable CLI and schema changes to both CLI package changelogs according to repository release conventions, and verify entries exclude implementation-only detail.
- [x] 4.6 Publish an external managed-file strategy overview with install, update, copy, merge, shared-target, and shared-transitive examples; verify Markdown lint and the Docusaurus production build pass.

## 5. End-to-End Validation

- [ ] 5.1 Add integration coverage for default plus named installs, independent values, distinct placement, ambiguous/default selection, single-instance update/uninstall, rename, unowned overwrite, owned conflict, and version-1 migration; verify the focused integration suite passes.
- [ ] 5.2 Run the complete CLI unit and integration test suites and schema validation, then fix only regressions caused by this change until all pass.
- [ ] 5.3 Restore with locked mode and publish the CLI with Native AOT for a supported runtime; verify lock-file validation and release compilation succeed.

## Continuation Notes

- Completed and verified: tasks 1.1-1.4, 2.1-2.6, and 4.6.
- Strategy refactor verification: all 19 `PackUpdatePlannerTests` passed.
- Planning baseline verification: 861 CLI unit tests passed after tasks 2.1-2.5.
- Documentation verification: Markdown lint, formatting, and Docusaurus production build passed; build emitted only expected warnings for disabled local `docs` and `blog` directories.
- Current stopping point: tasks 3.1 and 3.2 are partially implemented but not complete. Relevant edits include install, update, and uninstall handlers plus `Project/PackInstanceSelection.cs`; keep both tasks unchecked until all listed command cases pass.
- Last focused check passed: `PackInstallationPlannerTests/Plan_WhenSiblingConfigurationsExist_UsesCurrentInstanceDestination`.
- Resume by running the complete CLI unit suite to establish the current failure list, then finish task 3.1 before task 3.2. Continue with tasks 3.3-3.6, documentation tasks 4.1-4.5, and end-to-end validation.
- Preserve all current worktree changes; they contain the in-progress lock-v2, instance-planning, strategy-refactor, and documentation work.
