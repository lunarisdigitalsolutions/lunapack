## Purpose

Persist the exact resolved pack graph and managed-file ownership separately from
portable project configuration so lifecycle operations remain reproducible.

## ADDED Requirements

### Requirement: Persist a versioned resolved pack graph

LunaPack SHALL persist resolved installation state in `lunapack-lock.yml`, separate
from `lunapack.yml`. The lock file SHALL declare its own schema version and record
every resolved root and transitive pack exactly once by ID and exact Semantic
Version, each pack's configured-source provenance, its composite references,
and every managed target path with its installed SHA-256 digest. The lock file
SHALL not add source configuration to pack manifests.

#### Scenario: Lock a composite installation

- **WHEN** LunaPack installs a requested composite pack with transitive references
- **THEN** `lunapack-lock.yml` records the complete resolved graph, source
  provenance, and managed-file digests for all resolved packs

#### Scenario: Lock a contentless composite pack

- **WHEN** LunaPack installs a composite pack that declares references but no
  managed files
- **THEN** the lock file records the composite pack and its resolved references
  without managed-file entries for that pack

### Requirement: Update resolved state atomically

LunaPack SHALL update the lock file whenever it adds or removes pack state. A
lifecycle operation that cannot persist valid configuration and valid
lock state SHALL leave `lunapack.yml`, `lunapack-lock.yml`, and managed project files
unchanged.

#### Scenario: Refuse an invalid lock update

- **WHEN** a lifecycle operation cannot write schema-valid lock state
- **THEN** LunaPack reports a non-success result and preserves the previous
  configuration, lock file, and managed project files

### Requirement: Preserve only reachable transitive packs

LunaPack SHALL retain a transitive pack in `lunapack-lock.yml` while it remains
reachable from at least one requested root pack in `lunapack.yml`. LunaPack SHALL
remove a transitive pack's lock record and unchanged managed files only after
it is no longer reachable from any requested root.

#### Scenario: Remove an unshared composite dependency

- **WHEN** a requested composite pack is removed and one of its dependencies
  is not reachable from another requested root
- **THEN** LunaPack removes that dependency's lock record and unchanged managed
  files with the composite pack

#### Scenario: Retain a shared composite dependency

- **WHEN** a requested composite pack is removed and one of its dependencies
  remains reachable from another requested root
- **THEN** LunaPack retains that dependency's lock record and managed-file
  ownership
