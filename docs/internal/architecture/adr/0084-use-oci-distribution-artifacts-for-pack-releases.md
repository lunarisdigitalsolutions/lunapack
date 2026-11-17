---
status: accepted
date: 2026-11-16
decision-makers:
  - Lunaris Engineering
---

# ADR-0084: Use AOT-Safe OCI Distribution Artifacts

## Context and Problem Statement

LunaPack needs OCI registry distribution without Docker, an external registry
command, a registry-specific install engine, or a persistent content cache.
The CLI must remain compatible with Native AOT, and registry tags, metadata,
and catalog access cannot be assumed immutable or universally available.

## Decision Drivers

- Support generic OCI registries, GHCR, ACR, and Harbor through OCI Distribution.
- Preserve Native AOT compatibility without suppressing reflection warnings.
- Keep registry transport behind a domain-owned interface.
- Preserve existing pack validation, lifecycle, trust, and transaction boundaries.
- Bind installed content to verifiable registry and content evidence.

## Considered Options

- Use ORAS .NET and retain or suppress reflection-dependent code paths.
- Shell out to Docker or the ORAS CLI.
- Implement the required OCI Distribution operations over .NET HTTP.

## Decision Outcome

Chosen option: "Implement the required OCI Distribution operations over .NET
HTTP," because it keeps the executable AOT-compatible and makes the protocol
surface used by LunaPack explicit without adding external tool requirements.

Keep transport behind `IOciRegistryClient` and authentication behind the
shared source-authentication boundary. The adapter owns repository and tag
queries, pagination, manifest/blob transfers, upload sessions, digest
validation, and response classification. Metadata reads are bounded.
Pagination, bearer realms, and upload locations are validated before use;
authenticated requests remain scoped to the validated registry origin and
requested repository permissions.

Represent each release as an OCI image manifest with artifact type
`application/vnd.lunapack.pack.v1`, a bounded JSON config, and exactly one ZIP
content layer. The ZIP contains the complete validated authoring tree with
`pack.yml` at its root. Metadata cannot override the root manifest; the
repository-derived pack ID, requested version, config, annotations, and
manifest must agree. Lock provenance records the registry, repository, version
tag, manifest digest, and content digest. A locked release must match its
recorded digests.

Known pack IDs resolve directly to their repositories so explicit and locked
installs do not require global catalog access. Discovery reports incomplete
enumeration when registry permissions or provider behavior prevent a complete
catalog. OCI content is materialized in a private temporary operation directory
and is not retained as a persistent cache.

The CLI rejects an existing version tag, but OCI Distribution does not provide
a universal atomic create-if-absent operation. Publishers must configure
registry-side immutable tags to prevent later movement or concurrent
create-if-absent races.

## Consequences

- OCI releases use normal install, update, inspect, trust, and ownership paths.
- OCI transport has no Docker/ORAS CLI dependency and remains independent of
  optional ACR identity.
- Direct protocol and archive handling require focused boundary and integrity
  tests as the registry behavior LunaPack depends on is maintained in-house.
- Digest locks prove byte identity, not publisher identity or authorship.
- Registry enumeration permissions remain provider-specific.
- Older CLI versions cannot consume OCI source declarations; OCI support
  requires Luna 1.6.0 or later.

## Confirmation

OCI adapter and artifact tests verify repository/tag mapping, pagination,
media types, descriptor sizes, content and manifest digests, malformed
artifacts, cancellation, and timeouts. Native AOT publishing validates the
production compilation path. Pack-validation integration tests verify the
installable publishing packs. Live provider identities and native credential
store round-trips require configured registry and operating-system runners;
the project does not maintain a local-registry integration harness.

## More Information

See [ADR-0085](0085-bind-registry-credentials-to-source-identities.md),
[ADR-0039](0039-enforce-native-aot-cli-builds.md), the
`add-oci-registry-sources-and-publishing` OpenSpec change, and
`docs/developer/sources.md`.
