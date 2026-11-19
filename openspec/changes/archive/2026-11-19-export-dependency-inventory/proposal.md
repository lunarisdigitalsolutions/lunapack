# Proposal

## Why

Teams need a portable inventory of resolved LunaPack dependencies and managed content for procurement, compliance, and vulnerability review. Exporting locked source and content evidence in established SBOM formats avoids manual translation while preserving the lock file as the source of truth.

## What Changes

- Add `luna sbom` with an explicit target selecting CycloneDX 1.6 or 1.7 JSON, or SPDX 2.3 or 3.0.1 JSON.
- Export resolved packs, dependency edges, Luna Links, managed files and their recorded SHA-256 hashes, source identities, and recorded Git/OCI provenance.
- Support raw standard output or an optional output file. Generate documents offline from validated project lock state without rescanning managed-file contents, contacting sources, or changing project state.
- Keep inventory identifiers and collection ordering stable. Use current UTC time for SPDX-required creation timestamps and generation-specific document identities; guarantee stable inventory content and ordering rather than byte-identical output across runs.
- State in schema-supported document metadata, CLI help, and public documentation that an inventory is not a signature or attestation.
- Leave lock-file schema and lifecycle behavior unchanged.

## Capabilities

### New Capabilities

- `sbom-export`: Export deterministic, offline dependency inventories from resolved project lock state in versioned CycloneDX and SPDX JSON formats.

### Modified Capabilities

None. The change adds a read-only export capability and does not alter existing lock-file or lifecycle requirements.

## Impact

The CLI gains a new top-level command and format-specific serializers. Tests validate every supported target against pinned official JSON schemas. Developer command documentation and the CLI changelog describe the new behavior; an internal ADR records stable identifier and format-version contracts.
