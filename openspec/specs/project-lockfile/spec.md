# project-lockfile Specification

## Purpose

Persist the exact resolved pack graph and managed-file ownership separately from portable project configuration so lifecycle operations remain reproducible.

## Requirements

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

### Requirement: Persist declared and effective managed-file identities

The versioned `lunapack-lock.yml` schema SHALL record each managed file's manifest-declared target identity, effective project-relative target, installed SHA-256 digest, and existing strategy data. Lifecycle operations SHALL use the declared identity to correlate files across release changes and the effective target to locate files in the project. The lock schema SHALL evolve compatibly: LunaPack SHALL accept existing lock files, derive declared targets from their recorded destination behavior when possible, and write the current schema on the next successful lifecycle mutation. If a declared target cannot be derived safely, LunaPack SHALL fail without changing project files or state.

#### Scenario: Lock a remapped managed file

- **WHEN** installation remaps `docs/adr/template.md` to `docs/architecture/adr/_template.md`
- **THEN** the lock file records the declared target, effective target, and installed digest for that managed file

#### Scenario: Upgrade existing destination lock state

- **WHEN** an existing lock file records a directly requested pack destination and a lifecycle command successfully mutates its state
- **THEN** the resulting lock file uses the current schema and preserves the derived declared and effective targets

#### Scenario: Reject an unresolvable legacy lock record

- **WHEN** LunaPack cannot safely derive a declared target for a legacy managed file record
- **THEN** it returns a non-success result without changing project files, configuration, or lock state

### Requirement: Persist resolved Luna Link provenance and ownership

LunaPack SHALL persist every installed link exactly once in `lunapack-lock.yml`,
separate from portable definitions in `lunapack.yml`. Each record SHALL contain
the link name, immutable configured-source identity, canonical
installed-definition digest, and every selected file's normalized source path,
declared target identity, effective target, and installed SHA-256 digest.
Git-backed links SHALL also record the effective ref and resolved commit. Local
links SHALL use selected paths and content digests as authoritative resolution
evidence.

#### Scenario: Lock a Git-backed link

- **WHEN** LunaPack installs a link from a Git source
- **THEN** the lock file records its source identity, effective ref, resolved commit, definition digest, and complete per-file provenance and ownership

#### Scenario: Lock a local link

- **WHEN** LunaPack installs a link from a local source
- **THEN** the lock file records its source identity, definition digest, and each selected source path, target identity, effective target, and content digest

#### Scenario: Detect a changed definition

- **WHEN** a configured link's canonical definition digest differs from its installed lock record
- **THEN** LunaPack treats the installed link definition as changed without relying on file timestamps

### Requirement: Update resolved link state atomically

LunaPack SHALL update or remove link lock records in the same transaction as
corresponding project configuration and managed-file changes. A failure to write
schema-valid link state SHALL restore prior link-owned files, `lunapack.yml`, and
`lunapack-lock.yml`.

#### Scenario: Roll back an invalid link lock update

- **WHEN** an install, update, uninstall, or forced definition removal cannot persist valid resolved link state
- **THEN** LunaPack restores prior managed files, configuration, and lock state

### Requirement: Enforce unique managed-file ownership across roots

A project-relative target SHALL be owned by at most one resolved root, whether
that root is a pack or a link. LunaPack SHALL reject a lifecycle plan that
assigns one effective target to multiple roots unless an existing explicit
adoption rule permits the proposed ownership transition.

#### Scenario: Reject a link target owned by a pack

- **WHEN** a selected link file maps to a target already owned by an installed pack
- **THEN** LunaPack returns a non-success result without changing project files or state

#### Scenario: Reject duplicate link ownership

- **WHEN** two installed or proposed links map selected files to the same effective target
- **THEN** LunaPack returns a non-success result without changing project files or state

### Requirement: Update resolved state atomically

LunaPack SHALL update the lock file whenever it adds or removes pack state. A lifecycle operation that cannot persist valid configuration and valid lock state SHALL leave `lunapack.yml`, `lunapack-lock.yml`, and managed project files unchanged.

#### Scenario: Refuse an invalid lock update

- **WHEN** a lifecycle operation cannot write schema-valid lock state
- **THEN** LunaPack reports a non-success result and preserves the previous configuration, lock file, and managed project files

### Requirement: Preserve only reachable transitive packs

LunaPack SHALL retain a transitive pack in `lunapack-lock.yml` while it remains reachable from at least one requested root pack in `lunapack.yml`. LunaPack SHALL remove a transitive pack's lock record and unchanged managed files only after it is no longer reachable from any requested root.

#### Scenario: Remove an unshared composite dependency

- **WHEN** a requested composite pack is removed and one of its dependencies is not reachable from another requested root
- **THEN** LunaPack removes that dependency's lock record and unchanged managed files with the composite pack

#### Scenario: Retain a shared composite dependency

- **WHEN** a requested composite pack is removed and one of its dependencies remains reachable from another requested root
- **THEN** LunaPack retains that dependency's lock record and managed-file ownership
