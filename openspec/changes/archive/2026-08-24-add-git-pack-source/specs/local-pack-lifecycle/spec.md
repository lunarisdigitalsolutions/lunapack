## MODIFIED Requirements

### Requirement: Resolve composite pack references from configured sources

LunaPack SHALL recursively resolve every composite pack reference from the local and Git sources configured in the consuming project's `lunapack.yml`. Each composite reference SHALL resolve the declared ID and exact version using the same source-precedence rules as direct installation. LunaPack SHALL not read source configuration from a pack manifest.

#### Scenario: Install a composite pack from configured sources

- **WHEN** a user installs a composite pack whose referenced packs are present in configured sources
- **THEN** LunaPack resolves and installs the composite pack, all references, and their managed files

#### Scenario: Resolve a composite reference from the earliest configured source

- **WHEN** equal ID-and-version composite candidates exist in multiple configured sources
- **THEN** LunaPack selects the candidate from the earliest configured source

#### Scenario: Resolve a Git-sourced composite reference

- **WHEN** a Git-sourced composite pack references an exact pack version available from configured Git or local sources
- **THEN** LunaPack resolves that reference using the same configured-source precedence as a direct installation

#### Scenario: Refuse a missing composite reference

- **WHEN** a composite pack references an ID and version absent from configured sources
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

## ADDED Requirements

### Requirement: Install and update Git-sourced packs transactionally

LunaPack SHALL use the Git-source resolved commit selected during an install or update to read the complete selected pack content before it mutates managed files, `lunapack.yml`, or `lunapack-lock.yml`. A Git materialization or source-resolution failure SHALL leave project files and state unchanged.

#### Scenario: Refuse a failed Git-source installation

- **WHEN** a selected Git-source pack cannot be materialized at its resolved commit
- **THEN** LunaPack returns a non-success result without changing managed files, `lunapack.yml`, or `lunapack-lock.yml`

#### Scenario: Update a Git-sourced root pack

- **WHEN** a user updates an installed root pack and a higher version is available from its configured Git source
- **THEN** LunaPack applies the selected version and persists its Git resolution evidence with the updated lock state
