## MODIFIED Requirements

### Requirement: Persist a versioned resolved pack graph

LunaPack SHALL persist resolved installation state in `lunapack-lock.yml`, separate from `lunapack.yml`. The lock file SHALL declare its own schema version and record every resolved root and transitive pack exactly once by ID and exact Semantic Version, each pack's configured-source provenance, its composite references, and every managed target path with its installed SHA-256 digest. For a Git-sourced pack, provenance SHALL include the repository URL, configured ref when present, configured source path when present, and the immutable resolved commit SHA used to obtain the pack. The lock file SHALL not add source configuration to pack manifests.

#### Scenario: Lock a composite installation

- **WHEN** LunaPack installs a requested composite pack with transitive references
- **THEN** `lunapack-lock.yml` records the complete resolved graph, source provenance, and managed-file digests for all resolved packs

#### Scenario: Lock a contentless composite pack

- **WHEN** LunaPack installs a composite pack that declares references but no managed files
- **THEN** the lock file records the composite pack and its resolved references without managed-file entries for that pack

#### Scenario: Lock a Git-sourced pack

- **WHEN** LunaPack installs a pack resolved from a Git source
- **THEN** `lunapack-lock.yml` records the source repository URL, configured ref and path when present, and resolved commit SHA for that pack
