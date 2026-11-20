# Design

## Context

`ProjectStateStore.LoadAsync` parses, normalizes, and validates project configuration and lock state without contacting sources. `AuditService` is not an exporter input because it reads current target files and computes live modification status. The CLI is Native-AOT published, and project-relative output paths must follow `ProjectPath` and the mutation safeguards in ADR-0071.

## Goals / Non-Goals

**Goals:**

- Emit schema-valid CycloneDX 1.6/1.7 JSON and SPDX 2.3/3.0.1 JSON from the validated lock snapshot.
- Preserve exact pack dependencies, Luna Links, source identities, Git/OCI resolution evidence, and lock-recorded SHA-256 values.
- Produce stable identifiers and ordering without network access or changes to lock state.
- Confine file output to the selected workspace and replace it atomically without following existing aliases.

**Non-Goals:**

- Change lock-file schema, pack resolution, lifecycle behavior, or `luna audit`.
- Inspect current managed-file bytes, resolve remote sources, infer licenses, or invent missing source evidence.
- Emit XML, YAML, signatures, attestations, vulnerability results, or a full inventory of unmanaged workspace content.

## Decisions

### One lock-derived inventory with versioned format adapters

Load state through `ProjectStateStore`, then build a normalized inventory from `ProjectState.LockFile`. Represent resolved packs, their direct dependency edges, link records, managed files, and source evidence as distinct typed values. Format-specific adapters map this inventory to each target schema. Do not use catalog, Git, OCI, or live audit services.

### Stable cross-format identities

- Identify each pack by `pkg:generic/lunapack/<id>@<version>`. Validated lock state permits only one resolved pack per ID; source identity remains provenance rather than a second package identity.
- Identify each Luna Link by a stable `urn:lunapack:link:<name>` reference.
- Identify each managed file by a SHA-256-derived reference over its owning pack/link reference and normalized effective target, so equal paths under different owners remain distinct.
- Use pack PURLs as CycloneDX component references and SPDX package external references. Derive SPDX element IDs and relationship IDs from the canonical references; never generate random IDs.
- Map pack dependency edges to native dependency relationships. Map file ownership to native containment relationships where available, and retain an explicit owner reference in CycloneDX file properties.

Lock validation requires each effective managed-file target to be unique within
one pack or Luna Link. This keeps owner-and-target identifiers unambiguous and
prevents serializers from accepting duplicate elements with one stable
reference.

Include source identity and lock-recorded evidence as structured format properties/references: local source paths, Git repository/ref/path/resolved commit, OCI registry/repository/tag/manifest and content digests, and pack-defined external-source alias/fingerprint/ref/commit. Use only validated lock values; do not emit credentials. Use the existing SHA-256 values and do not rescan files. Do not claim license data absent from the lock.

### Deterministic output with required SPDX time metadata

Sort components, files, source properties, and relationships using ordinal keys. Derive inventory identifiers from stable lock evidence. Derive SPDX document namespaces and creation-info references from the target, canonical inventory content, and canonical UTC creation time so each generated document has a distinct identity. Omit optional random or time-dependent metadata such as a CycloneDX serial number or timestamp. Supply one current UTC creation timestamp to SPDX targets that require it; byte-for-byte repeatability is not promised across those exports, but all inventory identities, fields, and ordering remain stable.

Place the not-a-signature/not-an-attestation disclaimer in a schema-supported comment or property for each target. Keep the disclaimer in command help and the developer reference as well.

### Safe stdout and file output

Require `--target`; default output to raw JSON on the invocation's output writer, with no success message on standard output. Resolve an explicit `--output` against the selected workspace and validate it with `ProjectPath.NormalizeProjectRelativePath`. Write a complete UTF-8 document through `ProjectMutationPathSecurity.ReplaceFile` so alias ancestors are rejected and a hard-linked destination is replaced rather than truncated. Do not create missing parent directories; return an explicit filesystem error.

Before writing, reject a normalized output path that collides with `lunapack.yml`, `lunapack-lock.yml`, or any locked pack- or link-managed target. Compare paths using the filesystem's case-sensitivity rules. This preserves project state even when a consumer accidentally selects a protected path.

Use source-generated `System.Text.Json` contracts or equivalent AOT-safe typed serializers. Exercise each emitted target with structural and semantic contract tests using the .NET test dependencies already approved by the repository. Do not add a schema-validation package or runtime network requirement solely for SBOM conformance checks.

### Compatibility and documentation

The explicit target is the format-version compatibility boundary: `cyclonedx-1.6`, `cyclonedx-1.7`, `spdx-2.3`, and `spdx-3.0.1`. Verify each adapter's required structures and semantics with focused tests, and review standards changes before updating a target. Record the identity mapping and supported target contract in ADR-0102. Do not modify the lock format or require a lock migration.

## Risks / Trade-offs

- **[Schema drift]** A standard schema may change independently of this CLI → keep target versions explicit and maintain focused structural and semantic contract tests for each adapter.
- **[Sensitive provenance]** Inventory files can disclose private repository URLs or internal local paths → document the included provenance and never emit credential material.
- **[SPDX timestamps]** Required creation time makes SPDX output differ across invocations → include it in document-instance metadata and identities, while preserving stable inventory element identifiers.
- **[Format-specific limits]** One standard may not support every LunaPack relationship or disclaimer natively → use schema-valid properties/references and keep the canonical inventory model complete.
- **[Filesystem aliases]** A lexically safe output path can traverse a link or reparse point → use the existing mutation path security helper before replacing the output.
- **[Output collisions]** An output path could overwrite project state or a managed file → reject protected target collisions before writing.

## Migration Plan

No migration. The command reads existing valid lock state, writes only the requested export file when selected, and leaves project configuration and lock state unchanged.

## Open Questions

None. The target set, command name, inclusion of Luna Links, timestamp policy, and disclaimer placement are settled.
