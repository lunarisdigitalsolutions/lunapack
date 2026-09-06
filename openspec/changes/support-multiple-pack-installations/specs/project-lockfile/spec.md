## MODIFIED Requirements

### Requirement: Persist a versioned resolved pack graph

LunaPack SHALL persist resolved installation state in `lunapack-lock.yml`, separate from `lunapack.yml`. The lock file SHALL declare its own schema version, record every requested-root instance by pack ID and alias, and record compatible transitive packs as shared graph nodes by exact pack identity and Semantic Version. Each instance SHALL retain its root resolution, canonical placement, and directly owned managed target paths with installed SHA-256 digests. Shared pack records SHALL retain configured pack-source provenance, composite references, and shared transitive ownership. For a Git-sourced pack, pack-source provenance SHALL include the repository URL, configured ref when present, configured source path when present, and immutable resolved commit SHA used to obtain the pack.

For each used external source, the corresponding instance or shared pack record SHALL map its pack-local alias to the authoritative workspace source identifier and record the normalized fingerprint, canonical requested ref, and resolved commit. Every externally sourced managed file SHALL record its owning instance or shared pack, pack version, pack-local alias, authoritative workspace source identifier, source fingerprint, source-relative path, manifest-declared target, effective project-relative target, strategy data, and installed content hash. Consumer relationships SHALL exist only in `lunapack-lock.yml`; `lunapack.yml` SHALL not contain pack or link consumer metadata. The lock file SHALL not add source configuration to pack manifests beyond their declarative pack-local requirements.

#### Scenario: Lock a composite installation

- **WHEN** LunaPack installs a requested composite pack instance with transitive references
- **THEN** `lunapack-lock.yml` records the instance, complete resolved graph, pack and external-source provenance, instance-owned files, shared transitive ownership, and managed-file digests

#### Scenario: Lock sibling instances

- **WHEN** two instances of the same pack are installed at distinct effective targets
- **THEN** the lock records both aliases and associates each directly generated target with exactly one instance

#### Scenario: Lock a contentless composite pack

- **WHEN** LunaPack installs a composite pack that declares references but no managed files
- **THEN** the lock file records the instance and its resolved references without instance-owned managed-file entries

#### Scenario: Lock a Git-sourced pack

- **WHEN** LunaPack installs a pack resolved from a Git source
- **THEN** `lunapack-lock.yml` records the pack source repository URL, configured ref and path when present, and resolved commit SHA for that pack

#### Scenario: Lock an externally sourced file

- **WHEN** a pack instance installs a file selected through pack alias `upstream` and mapped to workspace source `awesome-copilot`
- **THEN** the lock records both identifiers, the normalized fingerprint, canonical ref, resolved commit, source path, declared and effective targets, instance owner, version, and installed hash

### Requirement: Enforce unique managed-file ownership across roots

A project-relative target SHALL be owned by at most one requested-root instance, shared transitive pack, or link. LunaPack SHALL reject a lifecycle plan that assigns one effective target to multiple owners unless an existing explicit adoption rule permits the proposed ownership transition. Overwriting an unowned file SHALL establish ownership without transferring ownership from another instance, pack, or link.

#### Scenario: Reject a link target owned by a pack

- **WHEN** a selected link file maps to a target already owned by an installed pack instance
- **THEN** LunaPack returns a non-success result without changing project files or state

#### Scenario: Reject duplicate link ownership

- **WHEN** two installed or proposed links map selected files to the same effective target
- **THEN** LunaPack returns a non-success result without changing project files or state

#### Scenario: Reject ownership shared by sibling instances

- **WHEN** two instances of one pack resolve a managed file to the same effective target
- **THEN** LunaPack returns a non-success result without changing project files or state

### Requirement: Preserve only reachable transitive packs

LunaPack SHALL retain a shared transitive pack in `lunapack-lock.yml` while it remains reachable from at least one requested-root instance in `lunapack.yml`. LunaPack SHALL remove a transitive pack's lock record and unchanged managed files only after it is no longer reachable from any requested-root instance.

#### Scenario: Remove an unshared composite dependency

- **WHEN** a requested-root instance is removed and one of its dependencies is not reachable from another requested-root instance
- **THEN** LunaPack removes that dependency's lock record and unchanged managed files with the instance

#### Scenario: Retain a shared composite dependency

- **WHEN** a requested-root instance is removed and one of its dependencies remains reachable from another requested-root instance
- **THEN** LunaPack retains that dependency's lock record and managed-file ownership

## ADDED Requirements

### Requirement: Migrate existing lock state to instances

LunaPack SHALL read existing supported lock files that do not contain explicit instance records and correlate each configured requested root to its resolved pack by pack ID as a default instance whose alias equals that ID. LunaPack SHALL write the current instance-aware schema only after a successful lifecycle mutation. If legacy state cannot be correlated unambiguously, LunaPack SHALL fail without changing project files, configuration, or lock state.

#### Scenario: Upgrade a legacy default root

- **WHEN** existing configuration and lock state contain one requested root and resolved pack with the same ID and a lifecycle mutation succeeds
- **THEN** LunaPack writes an explicit instance whose alias equals the pack ID while preserving version, provenance, targets, strategies, and digests

#### Scenario: Reject ambiguous legacy root state

- **WHEN** legacy configuration and lock state cannot identify one resolved root for a requested pack
- **THEN** LunaPack reports the ambiguity and leaves project files and state unchanged
