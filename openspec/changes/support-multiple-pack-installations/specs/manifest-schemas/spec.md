## MODIFIED Requirements

### Requirement: Publish project lock-file schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `lunapack-lock.yml`. The schema SHALL require its explicit supported schema version, requested-root instance records with pack IDs and aliases, and a resolved pack graph with exact pack identity and version, source provenance, composite references, and managed target-path SHA-256 records. Instance records SHALL allow canonical destination and remapping state and SHALL associate each directly generated managed file with one instance. Shared transitive records SHALL remain independently identifiable and reachable from instance roots. Git-sourced pack provenance SHALL record the repository URL, requested ref when configured, configured repository path when configured, and the resolved commit SHA. A pack that uses an external source SHALL record each used pack-local source alias, its authoritative workspace source identifier, normalized fingerprint, canonical requested ref, and resolved commit. Each externally sourced managed-file record SHALL identify its owning instance or shared pack, pack version, pack-local source alias, workspace source identifier, fingerprint, source-relative path, effective target, and installed content hash. The schema SHALL reject unknown lock schema versions, duplicate aliases within one pack, duplicate effective ownership, and incomplete resolved pack, instance, or external-source records. Existing supported lock records without explicit instances or external-source provenance SHALL remain readable under the documented migration rules.

#### Scenario: Validate resolved composite lock state

- **WHEN** the lock schema validates state produced for a composite pack instance and its transitive packs
- **THEN** validation succeeds

#### Scenario: Validate multiple instances

- **WHEN** the lock schema validates two instances of one pack with unique aliases, distinct placements, and disjoint owned targets
- **THEN** validation succeeds

#### Scenario: Validate Git-resolved lock state

- **WHEN** the lock schema validates a Git-sourced pack with its repository URL and resolved commit SHA
- **THEN** validation succeeds

#### Scenario: Validate external-source provenance

- **WHEN** the lock schema validates an externally sourced file and its pack alias mapping with all required identity, revision, path, ownership, and hash fields
- **THEN** validation succeeds

#### Scenario: Reject incomplete resolved state

- **WHEN** the lock schema validates a resolved pack or instance without required identity, source provenance, exact version, or managed-file digest
- **THEN** validation fails

#### Scenario: Reject Git provenance without a resolved commit

- **WHEN** the lock schema validates a Git-sourced pack or external-source record without a resolved commit SHA
- **THEN** validation fails

## ADDED Requirements

### Requirement: Publish project instance configuration schema

The `lunapack.yml` JSON Schema SHALL allow each requested pack entry to contain an optional instance alias using pack-ID syntax. It SHALL continue to allow entries without aliases as default instances and SHALL allow repeated pack IDs when aliases distinguish their entries. Schema-valid version-1 project files without aliases SHALL remain valid.

#### Scenario: Validate named requested roots

- **WHEN** project configuration declares two entries with one pack ID and distinct valid aliases
- **THEN** schema validation succeeds

#### Scenario: Validate an existing unnamed root

- **WHEN** project configuration declares a requested pack without an alias
- **THEN** schema validation succeeds and LunaPack interprets the alias as the pack ID

#### Scenario: Reject an invalid alias shape

- **WHEN** a requested pack entry declares an alias that does not satisfy pack-ID syntax
- **THEN** schema validation fails
