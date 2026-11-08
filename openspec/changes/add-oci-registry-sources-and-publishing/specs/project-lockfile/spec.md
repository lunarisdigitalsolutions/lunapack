# Spec Delta

## ADDED Requirements

### Requirement: Record exact OCI release provenance

Each resolved OCI pack SHALL record configured source name/type, normalized
registry URL/path, exact repository, manifest-derived version, resolved OCI
manifest digest, and ZIP content digest in normal lock state. Root and
transitive packs SHALL retain existing graph and ownership evidence.
Credentials and local ignore preferences SHALL never enter the lock file.

#### Scenario: Lock a private OCI release

- **WHEN** `dotnet-project` version `1.4.0` is installed from source `company`
- **THEN** its lock record contains source identity, repository, version, and
  verified manifest/content digests without authentication data

### Requirement: Materialize locked OCI content by digest

When an operation needs a previously locked release, Luna SHALL retrieve that
manifest by its recorded digest, validate content digests and pack identity,
and retain the locked source. A missing or altered locked artifact SHALL fail
clearly rather than silently following a moved tag or another source.

#### Scenario: Retrieve a release after its tag moves

- **WHEN** a version tag changed but its recorded manifest digest remains available
- **THEN** retrieval of the locked release uses the recorded digest, while online
  checks separately report the tag's immutability violation

#### Scenario: Reject missing locked content

- **WHEN** a registry no longer serves the required locked digest
- **THEN** Luna reports unavailable locked content without switching sources or
  changing installed state

### Requirement: Evolve lock state additively and atomically

Existing local/Git locks SHALL remain readable. OCI fields SHALL be required
only for OCI records and written through existing atomic lifecycle transactions.
Failed retrieval or state persistence SHALL preserve previous lock records and
managed-file ownership. No OCI cache SHALL be introduced through lock data.

#### Scenario: Add OCI to a mixed-source project

- **WHEN** an existing project installs its first OCI pack
- **THEN** its local/Git provenance is preserved and the new OCI record is
  persisted with all corresponding file ownership in one transaction
