---
status: superseded by ADR-0089
date: 2026-11-08
decision-makers:
  - Lunaris Engineering
---

# ADR-0088: Keep OCI Registry Operations Native AOT Compatible

## Context and Problem Statement

The CLI must publish and consume OCI artifacts from Native AOT executables.
ORAS .NET provides the authenticated transport and repository operations, but
its reflection-based list serialization and referrer-index publication paths
are incompatible with the CLI's AOT requirements.

## Decision Drivers

- Preserve Native AOT compatibility across supported CLI runtimes.
- Keep registry authentication, credential partitioning, and cancellation in
  the ORAS client.
- Bound registry metadata and validate pagination before following it.
- Avoid maintaining a second, independent HTTP authentication stack.

## Considered Options

- Use ORAS convenience methods and suppress their AOT diagnostics.
- Implement all OCI transport and authentication with a separate HTTP client.
- Use the authenticated ORAS client for affected protocol requests and retain
  ORAS repository APIs for supported manifest/blob operations.

## Decision Outcome

Chosen option: "Use the authenticated ORAS client for bounded direct protocol
requests," because it avoids reflection-dependent ORAS paths without duplicating
credential handling or abandoning the SDK boundary.

Catalog and tag endpoints are requested through the ORAS client and parsed with
`JsonDocument` under a 1 MiB per-page limit. Pagination accepts only same-origin
links and rejects repeated pages. Manifest PUT also uses the authenticated ORAS
client directly; it validates the descriptor, requests the registry's
pull/push repository scope, checks the response digest when supplied, and later
resolves the tag to verify publication completion. ORAS repository APIs remain
responsible for manifest and blob retrieval and blob upload.

## Consequences

- The small set of direct protocol requests must remain covered by adapter tests.
- Credential partitioning and redirect/challenge protections remain in ORAS.
- Catalog/tag parsing is bounded and does not depend on reflection-based JSON
  serialization.
- ORAS SDK changes may require updating the adapter and AOT validation.

## Confirmation

`OrasOciRegistryClientTests` cover bounded catalog/tag parsing, pagination,
cross-origin rejection, status classification, and manifest PUT content/digest
behavior. Native AOT publishing validates the production compilation path; the
authenticated local TLS registry integration verifies end-to-end publication
and consumption.

## More Information

See [ADR-0039](0039-enforce-native-aot-cli-builds.md),
[ADR-0084](0084-use-oci-distribution-artifacts-for-pack-releases.md),
the `add-oci-registry-sources-and-publishing` OpenSpec change, and
[`runtime.md`](../runtime.md).
