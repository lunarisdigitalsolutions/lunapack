# Pack-Defined External Git Sources Tasks

## 1. Schema and Model Foundations

- [ ] 1.1 Add failing schema tests for pack-defined Git sources, mandatory refs, rejected local sources and credentials, source-aware file, directory, and glob selectors, exclusions, flattening, and external-script rejection.
- [ ] 1.2 Extend `pack.schema.json`, `PackManifest`, YAML source conversion, model validation, and path normalization for alias-keyed Git sources and normalized selectors while retaining legacy `source`-only file entries.
- [ ] 1.3 Add failing schema and model tests for complete external-source mappings and per-file provenance, partial-record rejection, and existing lock-file compatibility.
- [ ] 1.4 Extend `lunapack-lock.schema.json`, `ProjectLockFile`, serialization contexts, normalization, and state validation with conditional external-source and file provenance fields.

## 2. Canonical Source Identity

- [ ] 2.1 Add unit tests for HTTPS, SSH, and scp-style repository normalization, GitHub case folding, `.git` and separator removal, credential rejection, base-path normalization, root representation, and local-versus-Git distinction.
- [ ] 2.2 Implement the shared source normalizer and `SourceFingerprint` value object, then route configured identity formatting and equality through sanitized canonical components.
- [ ] 2.3 Add Git ref-resolution tests for branches, tags, exact commits, missing refs, ambiguous short refs, canonical persistence, timeout, cancellation, and redacted errors.
- [ ] 2.4 Extend `GitRefResolver` to resolve complete branch or tag refs without ambiguity while preserving remote-HEAD behavior for workspace sources whose ref is omitted.
- [ ] 2.5 Re-key Git source cache identity and materialized content by normalized fingerprint plus resolved commit, with tests proving equivalent transports share cache entries.

## 3. Workspace Source Management

- [ ] 3.1 Add source-index validation that rejects duplicate fingerprints during project-state load and every source, link, install, update, rename, and write path; cover manually duplicated configuration and distinct base paths.
- [ ] 3.2 Extend workspace `sources add git` to canonicalize supplied refs and paths, reject unsafe or duplicate identities, and preserve optional-ref compatibility.
- [ ] 3.3 Implement `sources add github <id> <owner/repository> --ref <ref>` as command-only shorthand with strict input validation and normal Git persistence.
- [ ] 3.4 Implement atomic `sources rename`, including configuration, trust, lock, and link references, plus explicit repair for one unambiguous manual rename.
- [ ] 3.5 Change source removal to reject lock-file pack or link consumers, retain `rm` as canonical, support `remove` as an alias, and cover unchanged state on refusal.

## 4. Pack Authoring

- [ ] 4.1 Add compatibility tests that normalize legacy direct-file selectors and new direct or external selectors into one internal selector model without changing meaning.
- [ ] 4.2 Implement `pack add source git` and `pack add source github` with required ref, optional path and description, explicit manifest/workspace selection, canonical persistence, and atomic validation.
- [ ] 4.3 Extend `pack add file`, `directory`, and `glob` with source aliases, repeatable exclusions, flattening, target validation, and actionable unknown-alias failures.
- [ ] 4.4 Implement `pack remove source` and its `rm` alias with reference-count refusal, plus `pack sources` sanitized listing and reference counts.
- [ ] 4.5 Extend pack validation and inspection output for unknown aliases, unused-source warnings, source reachability, canonical refs, base paths, selector resolution, empty matches, and flattened target collisions.

## 5. Graph-Wide Source Planning and Consent

- [ ] 5.1 Add requirement-collector tests for transitive graphs, alias scope, conditional file selection, unused declarations, equivalent declarations, deterministic proposed identifiers, and collisions among proposed sources.
- [ ] 5.2 Implement an immutable external-source requirement plan that groups used declarations by fingerprint and maps each pack alias to an authoritative existing or proposed workspace identifier.
- [ ] 5.3 Add injectable source approval and identifier-prompt interfaces with interactive and deny implementations, sanitized combined formatting, default-no behavior, repeated identifier validation, and cancellation.
- [ ] 5.4 Integrate source planning into install and update preparation so reuse needs no approval, missing sources require all-or-nothing consent, and identifier conflicts fail when interaction is unavailable.
- [ ] 5.5 Add `--accept-sources` to install and update request handling with tests that permit only conflict-free additions and do not bypass validation, authentication, path safety, or script trust.
- [ ] 5.6 Extend dry-run models and formatting to show existing mappings, proposed additions, approval requirements, pack changes, and file actions without prompting or mutation.

## 6. External Content and Atomic Installation

- [ ] 6.1 Implement external-source materialization through the installed Git boundary, resolving each fingerprint once per commit and rejecting source-root or symbolic-link escape.
- [ ] 6.2 Extend managed-file planning to expand external single files, recursive directories, and globs in deterministic order, apply exclusions, preserve relative paths, and reject empty or flattened-collision selections.
- [ ] 6.3 Feed external files through existing conditions, templates, strategies, remapping, ownership, and target-conflict checks while retaining the declaring pack as owner.
- [ ] 6.4 Build external alias mappings and per-file provenance into candidate lock state and validate cross-references before applying managed-file actions.
- [ ] 6.5 Extend lifecycle transaction tests to prove rejected approval, identifier cancellation, Git failure, selector failure, target conflict, and state-write failure retain no proposed source, managed-file, or lock changes.

## 7. Update, Outdated, Audit, and Uninstall

- [ ] 7.1 Add source-drift comparison against locked fingerprints and block update with sanitized locked-versus-configured diagnostics until separately accepted.
- [ ] 7.2 Extend update planning to refresh symbolic external refs at unchanged pack versions, compare selected paths and hashes, add or remove glob members, and remove obsolete lock consumers without removing configured sources.
- [ ] 7.3 Extend `outdated` with external-content reasons, dependency and glob changes, missing or drifted source detection, irrelevant-commit suppression, and cache-only `--offline` reporting.
- [ ] 7.4 Extend audit models and formatting with pack aliases, workspace identifiers, fingerprints, canonical refs, commits, source paths, target paths, ownership, and all specified missing or drift states.
- [ ] 7.5 Preserve existing uninstall modification protections for external files and add optional source-cleanup guidance only after the last pack and link consumer is removed.

## 8. Guidance and Documentation

- [ ] 8.1 Extend `NextStepAdvisor` and formatter tests for pack initialization, source creation, external content creation, source reuse, rejected approval, install, update, and uninstall, enforcing the three-action limit and no update cleanup suggestion.
- [ ] 8.2 Create ADR-0051 for fingerprint authority, alias mapping, graph-wide consent, transaction boundaries, selector compatibility, and consumer-aware removal; mark ADR-0047 superseded and update the ADR index.
- [ ] 8.3 Update `docs/internal` architecture, path handling, lifecycle transaction, cache, and security guidance with implementation boundaries and threat controls.
- [ ] 8.4 Update `docs/product` requirements and `docs/developer` pack authoring, source, install, update, audit, schema, and troubleshooting guidance without presenting behavior as shipped before implementation completes.
- [ ] 8.5 Add an externally observable entry to `CHANGELOG.md` covering pack-defined external Git sources, consent, provenance, and source-management compatibility.

## 9. Verification

- [ ] 9.1 Validate all JSON schemas and fixtures, including legacy manifests and lock files plus new invalid traversal, credential, duplicate-fingerprint, and partial-provenance cases.
- [ ] 9.2 Run focused unit tests for source normalization, ref resolution, manifest authoring, graph planning, consent, selector expansion, lock validation, transactions, updates, audit, and guidance.
- [ ] 9.3 Add and run CLI integration tests for direct and transitive external sources, source reuse, interactive rejection, `--accept-sources`, identifier conflicts, dry runs, update drift, offline outdated checks, audit, uninstall, rename, and removal refusal.
- [ ] 9.4 Run CSharpier and analyzer validation, then execute the complete CLI unit and integration test suites.
- [ ] 9.5 Restore with locked dependencies and publish the CLI in Release Native AOT mode for each supported runtime used by repository validation.
