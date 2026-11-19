---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0093: Export Versioned Lock Inventories

## Context and Problem Statement

Consumers need portable evidence about LunaPack dependencies, Luna Links, and
managed-file ownership without contacting configured sources or rescanning
workspace content. The export contract must select an explicit standard version,
remain reproducible for unchanged lock state, and avoid implying that the
document authenticates or attests to its contents.

## Decision Drivers

- Keep `lunapack-lock.yml` as the sole evidence source for the inventory.
- Support established JSON consumers without coupling the CLI runtime to a
  schema validator or external network service.
- Keep references stable across repeated exports and format adapters.
- Redact URL credentials, queries, and fragments from exported provenance.
- Constrain explicit file output to the selected workspace and preserve project
  state.

## Considered Options

- Emit one flexible, unversioned LunaPack JSON format.
- Select one external SBOM standard and omit other versions.
- Expose explicit CycloneDX and SPDX version targets from one canonical inventory.

## Decision Outcome

Chosen option: "Expose explicit CycloneDX and SPDX version targets from one
canonical inventory," because consumers receive schema-defined documents while
LunaPack retains one lock-derived representation and one stable identity
contract.

`luna sbom` supports `cyclonedx-1.6`, `cyclonedx-1.7`, `spdx-2.3`, and
`spdx-3.0.1`. CycloneDX exports omit optional generation time. SPDX exports
include the standard-required current UTC creation time. The timestamp does not
participate in package, file, link, or relationship identities, but does
participate in identifiers that represent one generated SPDX document.

The canonical identity mapping is:

- Resolved pack:
  `pkg:generic/lunapack/<escaped-id>@<escaped-version>`.
- Luna Link: `urn:lunapack:link:<escaped-name>`.
- Managed file:
  `urn:lunapack:file:sha256:<sha256(owner-reference + NUL + effective-target)>`.
- SPDX 2 element:
  `SPDXRef-<kind>-<sha256(kind + NUL + canonical-reference)>`.
- SPDX 3 element:
  `urn:lunapack:spdx:3.0.1:<kind>:<sha256(kind + NUL + canonical-reference)>`.
- SPDX document ID:
  `https://lunapack.dev/sbom/spdx-3.0.1/<document-fingerprint>#document`.
- SPDX creation ID:
  `https://lunapack.dev/sbom/spdx-3.0.1/<document-fingerprint>#creation-info`.

Lock validation requires unique effective managed-file targets within each pack
and Luna Link. The same target can remain under different owners because the
owner reference participates in the file identity.

The inventory fingerprint covers the serialized canonical inventory and target
name. The SPDX document fingerprint additionally covers canonical UTC creation
time so separately generated documents cannot reuse one namespace or creation
identity. Every inventory collection and relationship is sorted by ordinal
identity.

### Consequences

- Good, because consumers can select an exact, validated schema version.
- Good, because pack, link, file, relationship, and document references remain
  stable without random identifiers or generation time.
- Good, because exports work offline and do not reinterpret the current managed
  files as lock evidence.
- Good, because URL credentials and non-identity query or fragment material do
  not appear in exported provenance.
- Bad, because a separate adapter must be maintained when a supported standard
  changes.
- Bad, because provenance can still disclose source coordinates and local paths;
  consumers must treat exports as potentially sensitive.
- Neutral, because SPDX creation metadata and document-instance identifiers vary
  across generations while inventory identities remain stable.

### Confirmation

- Unit tests validate every supported target against a pinned official JSON
  schema and check deterministic identities and timestamp boundaries.
- Unit, integration, and security tests verify raw stdout, error routing,
  protected paths, traversal rejection, alias rejection, and state preservation.
- The public command reference describes the target contract, lock-derived
  scope, provenance exposure, output rules, and non-attestation limitation.

## Pros and Cons of the Options

### Emit one flexible, unversioned LunaPack JSON format

- Good, because format-specific mapping is not required.
- Bad, because consumers need LunaPack-specific tooling and compatibility
  validation.
- Bad, because format evolution is difficult to identify in automation.

### Select one external SBOM standard

- Good, because the adapter surface is smallest.
- Bad, because it excludes consumers standardized on another supported format.
- Bad, because one format cannot guarantee every LunaPack evidence field has a
  native representation.

### Expose explicit CycloneDX and SPDX version targets

- Good, because target names form a clear compatibility boundary.
- Good, because established tooling can consume the documents directly.
- Bad, because four schema variants require ongoing conformance tests.

## More Information

- [OpenSpec SBOM export requirement](../../../../openspec/specs/sbom-export/spec.md)
- [ADR-0037: Canonicalize Persisted Project Paths](0037-canonicalize-persisted-project-paths.md)
- [ADR-0071: Reject Filesystem Aliases at Mutation Boundaries](0071-reject-filesystem-aliases-at-mutation-boundaries.md)
- GitHub issue #81
