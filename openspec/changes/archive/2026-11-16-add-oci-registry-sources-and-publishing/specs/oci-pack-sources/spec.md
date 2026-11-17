# Spec Delta

## Purpose

Distribute and consume LunaPack releases through OCI registries using portable
pack artifacts and the existing source and pack lifecycle.

## ADDED Requirements

### Requirement: Map OCI packs to source-scoped repositories

LunaPack SHALL map a pack to `{registry}/{source-path}/{pack-id}:{version}`,
omitting the path separator when the source path is empty. Each pack SHALL own
one repository. Registry identity and repository path SHALL remain separate.
Invalid repository or tag mappings SHALL fail without publication or mutation.

#### Scenario: Map a nested source path

- **WHEN** source `company` uses `https://lunapack.azurecr.io`, path
  `customers/acme`, and pack `dotnet-project` version `1.4.0`
- **THEN** its release reference is
  `lunapack.azurecr.io/customers/acme/dotnet-project:1.4.0`

#### Scenario: Map an empty source path

- **WHEN** the same pack uses a source with no path
- **THEN** its repository is `lunapack.azurecr.io/dotnet-project`

#### Scenario: Reject an unmappable identity

- **WHEN** a pack ID or version cannot be represented losslessly as a supported
  OCI repository name or version tag
- **THEN** Luna reports the invalid manifest value without silently renaming it

### Requirement: Access registries without external container tools

OCI read and publish operations SHALL work with OCI Distribution compatible
registries without Docker, Docker Desktop, ORAS CLI, or a container runtime.
Username/password/token authentication SHALL not require Azure CLI.

#### Scenario: Use a generic registry on a clean machine

- **WHEN** Luna accesses an accessible OCI registry without these external tools
- **THEN** discovery, retrieval, and publication use Luna's native OCI support

### Requirement: Discover compatible repositories under a source path

Luna SHALL discover accessible repositories under the exact configured source
path boundary and list their compatible LunaPack releases. Pagination SHALL be
honored. Non-LunaPack artifacts and invalid version tags SHALL not become pack
candidates; rejected LunaPack content SHALL produce source-scoped diagnostics.

#### Scenario: Keep sibling paths separate

- **WHEN** source path `packs` can enumerate `packs/backend` and `packs-old/other`
- **THEN** only `packs/backend` participates in that source's catalog

#### Scenario: Enumerate multiple pages

- **WHEN** repository or tag enumeration returns paginated results
- **THEN** Luna considers all accessible pages, retaining normal version ordering

### Requirement: Resolve known repositories without catalog permission

Luna SHALL query explicitly requested and locked pack repositories directly
when catalog enumeration is unavailable. It SHALL report discovery limitations
without claiming a complete catalog or inventing packs. Installation of a known
accessible release SHALL not require global catalog permission.

#### Scenario: Install with repository-scoped credentials

- **WHEN** credentials can read `packs/backend` but cannot enumerate the registry
  and the user requests `backend`
- **THEN** Luna queries that repository directly and can resolve its releases

#### Scenario: Report incomplete discovery

- **WHEN** repository enumeration is denied but other configured sources work
- **THEN** Luna reports limited discovery for the OCI source and preserves other
  results without calling the OCI source empty

### Requirement: Identify LunaPack OCI artifacts

A v1 LunaPack artifact SHALL declare artifact type
`application/vnd.lunapack.pack.v1` and exactly one content layer with media type
`application/vnd.lunapack.pack.content.v1+zip`. Metadata SHALL identify pack ID,
version, and format version. The root `pack.yml` SHALL remain authoritative;
metadata, requested identity, and manifest identity SHALL agree.

#### Scenario: Reject an unrelated OCI artifact

- **WHEN** a selected tag identifies an image or unsupported artifact type
- **THEN** Luna rejects it as unsupported before applying pack content

#### Scenario: Reject inconsistent identity

- **WHEN** artifact metadata claims `backend` version `1.0.0` but its root
  manifest declares a different ID or version
- **THEN** Luna rejects the release without changing project state

#### Scenario: Reject invalid structure

- **WHEN** metadata is invalid, the format version is unsupported, or there is
  no single supported ZIP content layer
- **THEN** Luna identifies the source and reports the structural error

### Requirement: Preserve complete pack content in one ZIP

The ZIP SHALL preserve the complete validated pack directory hierarchy,
including root manifest, README, nested files, scripts, and dotfiles.
Publication SHALL not create a layer per file or require external archive tools.
Available title, description, source repository, and source revision SHALL be
represented as appropriate OCI annotations.

#### Scenario: Round-trip a pack directory

- **WHEN** a pack containing `pack.yml`, `README.md`, `files/.editorconfig`, and
  `scripts/setup.ps1` is published and retrieved
- **THEN** the extracted pack preserves their paths and bytes

### Requirement: Verify content before trusting extraction

Luna SHALL validate OCI descriptor sizes and digests for metadata and ZIP
content before trusting them. Missing content, corrupt ZIPs, invalid manifests,
and digest mismatches SHALL produce actionable source-specific failures and
SHALL NOT mutate managed files, configuration, or lock state.

#### Scenario: Reject a substituted blob

- **WHEN** the registry returns ZIP bytes whose digest differs from the descriptor
- **THEN** Luna rejects the blob before extraction or lifecycle application

### Requirement: Confine and bound ZIP extraction

Every normalized archive entry SHALL remain under the temporary extraction
root. Luna SHALL reject absolute, drive-qualified, escaping, duplicate,
conflicting, link-like, or malformed entries and enforce documented finite
entry-count, download, per-entry, and total expansion limits.

#### Scenario: Reject unsafe archive paths

- **WHEN** an archive contains `../../../secret`, `/root/file`,
  `C:\Windows\something`, or a mixed-separator traversal
- **THEN** Luna rejects the archive and writes nothing outside its extraction root

#### Scenario: Reject ambiguous or excessive content

- **WHEN** normalized entries collide, a file conflicts with a directory, or an
  archive exceeds an extraction limit
- **THEN** Luna reports unsafe ZIP content and removes temporary extracted files

### Requirement: Query current state without a persistent OCI cache

Commands needing current OCI state SHALL query the registry directly.
Downloaded artifacts, extracted directories, and publish archives SHALL be
temporary and cleaned on success, failure, and cancellation. Project files
and digest provenance in normal lock state SHALL not be treated as an OCI cache.

#### Scenario: Observe a newly published version

- **WHEN** a release is published between two online discovery or update checks
- **THEN** the second check queries current registry state rather than stale cache

#### Scenario: Clean up cancelled retrieval

- **WHEN** a pull is cancelled during download, validation, or extraction
- **THEN** Luna cleans temporary resources and preserves project state

### Requirement: Report OCI access and artifact errors precisely

Errors SHALL identify the source and distinguish authentication, permission,
unavailability, timeout, DNS, TLS, repository/pack/version absence, unsupported
artifact, invalid manifest, missing ZIP layer, digest mismatch, corrupt ZIP,
and unsafe ZIP content. Diagnostics SHALL not contain credentials.

#### Scenario: Distinguish permissions from absence

- **WHEN** an authenticated request is forbidden
- **THEN** Luna reports insufficient permission rather than claiming the pack
  does not exist or repeatedly prompting for the same credentials
