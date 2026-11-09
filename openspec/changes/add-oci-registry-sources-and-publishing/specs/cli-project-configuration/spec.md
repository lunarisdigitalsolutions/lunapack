# Spec Delta

## ADDED Requirements

### Requirement: Register an OCI source without contacting it

`luna sources add oci <name> <registry-url> [--path <path>]` SHALL persist
`type: oci`, a normalized registry URL, and an optional normalized repository
path. It SHALL reject duplicate names/fingerprints, unsafe paths, embedded
credentials, and invalid endpoints before mutation. Registration SHALL require
no network availability or authentication.

#### Scenario: Add independent paths on one registry

- **WHEN** a user adds `company` at `https://lunapack.azurecr.io --path engineering`
  and `customer` at the same URL with `--path customers/acme`
- **THEN** both sources are saved independently without requesting credentials

#### Scenario: Normalize tolerant endpoint input

- **WHEN** a source URL includes a safe repository path and no explicit path
- **THEN** Luna separates the endpoint and normalized path in persisted state

#### Scenario: Reject conflicting embedded paths

- **WHEN** an embedded URL path disagrees with an explicit `--path`
- **THEN** Luna reports the conflict without guessing, contacting the registry,
  or changing configuration

### Requirement: Canonicalize OCI endpoint and path identity

OCI identity SHALL include the normalized endpoint and repository path.
Equivalent endpoints and safe path forms SHALL share a fingerprint, independent
of source name. Different paths SHALL remain distinct. Paths SHALL preserve
OCI-valid case/segment semantics and reject escaping, absolute, or invalid
repository components rather than silently changing repository identity.

#### Scenario: Reject an equivalent duplicate

- **WHEN** two sources have equivalent host casing, trailing separators, and
  normalized paths
- **THEN** Luna rejects the duplicate fingerprint even if their names differ

#### Scenario: Accept nested and empty paths

- **WHEN** sources use `security/packs`, a safe slash-normalized equivalent,
  or no path
- **THEN** Luna persists the canonical path or omits the empty path

### Requirement: Preserve local state across source rename

OCI source rename SHALL retain existing atomic configuration, lock, link,
and trust-reference semantics. It SHALL preserve ignore state for the same
fingerprint. Credentials bound to the old source name SHALL not silently become
credentials for the renamed source; later access SHALL resolve authentication
under the new identity.

#### Scenario: Rename a locked ignored OCI source

- **WHEN** `company` becomes `engineering`
- **THEN** lock references use `engineering`, the fingerprint and resolved digest
  remain unchanged, ignore state follows that identity, and no secrets are copied
  into project state

## MODIFIED Requirements

### Requirement: List configured source names

`luna sources list` SHALL include each source's name, type, and existing
type-specific location details in configured precedence order. OCI entries
SHALL show registry and repository path. Ignored entries SHALL remain visible
and SHALL be marked `ignored locally` without source access.

#### Scenario: List local and Git source names

- **WHEN** a project contains named local and Git sources and the user runs `luna sources list`
- **THEN** each result shows its source name together with its type and path or repository details

#### Scenario: List an ignored OCI source

- **WHEN** a configured OCI source is ignored for the current project user
- **THEN** listing shows its name, `oci` type, registry/path, and local ignored
  state without contacting the registry
