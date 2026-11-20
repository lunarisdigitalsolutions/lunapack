# Tasks

## 1. Lock-Derived Inventory

- [x] 1.1 Add a typed inventory model and builder for resolved pack dependencies, Luna Links, managed files, source identities, Git/OCI provenance, and recorded hashes; verify mappings and absent optional evidence with focused unit tests.
- [x] 1.2 Define stable pack, link, file, and relationship identifiers and deterministic ordering; verify repeated serialization preserves identifiers, content, and order apart from SPDX-required timestamps.

## 2. Versioned SBOM Formats

- [x] 2.1 Implement AOT-safe CycloneDX 1.6/1.7 JSON and SPDX 2.3/3.0.1 JSON serializers, including source evidence and the schema-supported non-attestation disclaimer; verify every target's required structure, relationships, hashes, and evidence with focused tests.
- [x] 2.2 Include current UTC creation timestamps only where required by SPDX, use them only in document-generation metadata and identities, and keep inventory identifiers stable; verify timestamps and both stability boundaries.

## 3. CLI and Safe Output

- [x] 3.1 Register `luna sbom --target <target> [--output <path>]`; verify help, target validation, and raw stdout with no success chatter through CLI tests.
- [x] 3.2 Resolve output paths with `ProjectPath` and write through `ProjectMutationPathSecurity`; verify protected-path collision, rooted/traversal rejection, alias rejection, I/O errors, and unchanged project state with unit and real-filesystem tests.

## 4. Documentation and Governance

- [x] 4.1 Add ADR-0102 and its index entry for target versions and stable identifier mappings; verify required ADR sections and the index link.
- [x] 4.2 Document command syntax, targets, lock-derived scope, provenance exposure, timestamp behavior, output paths, and non-attestation limits in `docs/developer/cli/commands.md`; verify links and rendered documentation.
- [x] 4.3 Add the externally observable CLI change under `Unreleased` in `projects/cli/CHANGELOG.md`; verify it follows the existing consumer-facing format.

## 5. Integration Validation

- [x] 5.1 Run the CLI solution build and all unit, integration, and security tests; confirm every supported target validates and output operations preserve lock/configuration state.
- [x] 5.2 Run OpenSpec validation and documentation lint/build checks; confirm the change artifacts and public documentation pass.

## Workflow follow-up

- Archive `export-dependency-inventory` after implementation and validation; verify the archived `sbom-export` specification.
- Commit the completed changes, push the feature branch, and create a pull request referencing issue #81.
