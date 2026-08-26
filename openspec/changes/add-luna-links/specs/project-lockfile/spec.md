# Project Lockfile Delta Specification

## ADDED Requirements

### Requirement: Persist resolved Luna Link provenance and ownership

LunaPack SHALL persist every installed link exactly once in `lunapack-lock.yml`, separate from portable definitions in `lunapack.yml`. Each record SHALL contain the link name, immutable configured-source identity, canonical installed-definition digest, and every selected file's normalized source path, declared target identity, effective target, and installed SHA-256 digest. Git-backed links SHALL also record the effective ref and resolved commit. Local links SHALL use selected paths and content digests as authoritative resolution evidence.

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

LunaPack SHALL update or remove link lock records in the same transaction as corresponding project configuration and managed-file changes. A failure to write schema-valid link state SHALL restore prior link-owned files, `lunapack.yml`, and `lunapack-lock.yml`.

#### Scenario: Roll back an invalid link lock update

- **WHEN** an install, update, uninstall, or forced definition removal cannot persist valid resolved link state
- **THEN** LunaPack restores prior managed files, configuration, and lock state

### Requirement: Enforce unique managed-file ownership across roots

A project-relative target SHALL be owned by at most one resolved root, whether that root is a pack or a link. LunaPack SHALL reject a lifecycle plan that assigns one effective target to multiple roots unless an existing explicit adoption rule permits the proposed ownership transition.

#### Scenario: Reject a link target owned by a pack

- **WHEN** a selected link file maps to a target already owned by an installed pack
- **THEN** LunaPack returns a non-success result without changing project files or state

#### Scenario: Reject duplicate link ownership

- **WHEN** two installed or proposed links map selected files to the same effective target
- **THEN** LunaPack returns a non-success result without changing project files or state
