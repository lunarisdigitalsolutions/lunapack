---
status: accepted
date: 2026-11-09
decision-makers:
  - Lunaris Engineering
---

# ADR-0089: Use AOT-Safe OCI HTTP and Native Credential Stores

## Context and Problem Statement

The CLI must consume and publish OCI artifacts from Native AOT executables.
ORAS .NET and `ktsu.CredentialCache` introduced reflection-dependent code paths
that prevented a warning-free AOT publish. ADR-0084 selected ORAS .NET for the
transport boundary, and ADR-0088 retained its authenticated client for selected
protocol requests. Neither approach removes the reflection warnings from the
remaining repository, serialization, and credential-cache APIs.

## Decision Drivers

- Keep all supported Native AOT publish targets free of reflection warnings.
- Preserve the domain-owned `IOciRegistryClient` boundary and OCI Distribution
  behavior without Docker or external registry commands.
- Bound registry metadata and authenticate only to validated registry endpoints
  and repository scopes.
- Store validated credentials only through operating-system credential stores.
- Keep optional Azure Identity outside generic OCI source and transport policy.

## Considered Options

- Keep ORAS and `ktsu.CredentialCache`, rooting reflection metadata or suppressing
  their AOT diagnostics.
- Replace them with another OCI SDK and credential-cache package.
- Implement OCI Distribution over .NET HTTP and use narrow platform-native
  credential-store adapters.

## Decision Outcome

Chosen option: "Implement OCI Distribution over .NET HTTP and use native
credential-store adapters," because this removes the reflection-dependent
transport and serialization paths while retaining AOT-compatible, OS-protected
credential storage.

The registry client owns catalog/tag pagination, manifest and blob transfer,
upload sessions, digest validation, and response classification. Metadata reads
are bounded. Pagination, authentication realms, and upload locations must stay
on the validated registry origin; bearer scopes cannot exceed the operation's
requested repository permissions. This prevents forwarding credentials to a
third-party token realm; registries that require cross-origin bearer realms are
not compatible with authenticated access. Authentication remains
operation-scoped and source-bound through `IOciRegistryClient` and
`IOciAccessTokenProvider`.

The credential store uses Windows Credential Manager, macOS Keychain, and
Linux Secret Service through direct native APIs. It stores a bounded,
length-prefixed UTF-8 payload under a hash of the source credential identity.
Linux requires the `libsecret` and GLib runtime libraries and an available
Secret Service. No plaintext file or colocated-key fallback is permitted. The
optional ACR provider continues to use Azure Identity behind the access-token
provider boundary.

This decision supersedes ADR-0084's ORAS transport choice and ADR-0088's
authenticated ORAS adapter. The artifact and lock contracts established by
ADR-0084 remain unchanged.

### Consequences

- Good, because the direct HTTP adapter and authentication parser have focused
  protocol, failure, and local-registry tests.
- Good, because credentials stay in native OS stores and never enter project or
  user configuration.
- Bad, because native credential-store interop must be tested on Windows,
  macOS, and Linux, and platform store failures remain explicit.
- Bad, because Linux users need Secret Service, `libsecret`, and GLib; Windows
  Credential Manager enforces its native credential-blob size limit.
- Bad, because removing the SDK reduces dependencies but makes LunaPack
  responsible for maintaining the OCI Distribution details it uses.

### Confirmation

Run OCI client unit tests for authentication, pagination, upload-location
validation, integrity checks, cancellation, and failure classification. Run the
authenticated local TLS registry integration suite and Native AOT publishes for
every supported runtime identifier; exercise native credential-store
round-trips and failures on each supported operating system.

## Pros and Cons of the Options

### Keep the current reflection-dependent packages

- Good, because SDKs provide a broad set of registry and credential-store
  operations.
- Bad, because their AOT-incompatible code remains in the publish graph.

### Use another OCI SDK and credential package

- Good, because an alternative may provide a higher-level protocol boundary.
- Bad, because it adds dependency and AOT compatibility risks without removing
  LunaPack's need to validate redirects, scopes, payload sizes, and digests.

### Use direct HTTP and native credential APIs

- Good, because the required protocol paths and OS storage boundaries are
  explicit and testable without reflection-based serialization.
- Bad, because LunaPack must maintain the adapter and platform interop code.

## More Information

See [ADR-0039](0039-enforce-native-aot-cli-builds.md),
[ADR-0084](0084-use-oci-distribution-artifacts-for-pack-releases.md),
[ADR-0088](0088-keep-oci-registry-operations-native-aot-compatible.md), and
the `add-oci-registry-sources-and-publishing` OpenSpec change.
