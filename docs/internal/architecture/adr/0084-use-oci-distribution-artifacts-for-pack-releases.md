---
status: superseded by ADR-0089
date: 2026-11-08
decision-makers:
  - Lunaris Engineering
---

# ADR-0084: Use OCI Distribution Artifacts for Pack Releases

## Context and Problem Statement

LunaPack needs private registry distribution without a registry-specific pack
installation engine, Docker dependency, or persistent content cache. Registry
tags and metadata are mutable, and registry-wide catalog enumeration is not
available to every identity.

## Decision Drivers

- Reuse OCI Distribution for generic registries, GHCR, ACR, and Harbor.
- Keep ORAS SDK types inside a transport adapter.
- Preserve the existing pack validation, planning, trust, and transaction
  boundaries.
- Bind installed content to verifiable registry and content evidence.
- Reject publication races rather than claim client-side tag immutability.

## Considered Options

- Add a hosted LunaPack catalog and registry-specific clients.
- Shell out to Docker or the ORAS CLI.
- Use ORAS .NET behind a domain-owned registry interface.

## Decision Outcome

Chosen option: "Use ORAS .NET behind a domain-owned registry interface,"
because OCI transport can serve both publication and consumption while the
existing lifecycle remains authoritative.

Each release is an OCI image manifest with a LunaPack artifact type, a bounded
JSON config, and exactly one ZIP content layer. The ZIP contains the complete
validated authoring tree with `pack.yml` at its root. The adapter owns
repository, tag, manifest, and blob operations; domain code owns pack IDs,
versions, artifact validation, and source outcomes.

Configured OCI source identity is the normalized HTTPS origin and optional
repository prefix. Known pack IDs resolve directly to their repository so a
registry can support locked or explicit installs without granting global
catalog access. Discovery may report incomplete enumeration.

Lock provenance stores the registry, repository, version tag, manifest digest,
content digest, and content size. A locked release must match its recorded
digests. The CLI rejects an existing version tag, but publishers must configure
registry-side immutable tags to prevent later movement or concurrent
create-if-absent races. OCI content is materialized in a private temporary
operation directory and is not retained as a persistent cache.

## Consequences

- Generic OCI transport and optional ACR authentication remain separate
  adapters.
- OCI releases use normal install, update, inspect, trust, and ownership paths.
- Digest locks prove byte identity, not publisher identity or authorship.
- Registry enumeration permissions remain provider-specific.
- Older CLI versions cannot consume OCI source declarations; OCI support
  requires Luna 1.6.0 or later.

## Confirmation

Adapter and artifact tests verify repository/tag mapping, media types, descriptor
sizes, content and manifest digests, malformed artifacts, and timeouts. A
TLS-enabled local registry integration test exercises publish, discovery,
install, update, duplicate rejection, and cleanup.

## More Information

See the `add-oci-registry-sources-and-publishing` OpenSpec change and
`docs/developer/sources.md`.
