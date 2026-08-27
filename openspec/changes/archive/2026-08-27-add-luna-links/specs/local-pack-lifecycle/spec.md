# Local Pack Lifecycle Delta Specification

## ADDED Requirements

### Requirement: Install links through the managed-file lifecycle

`luna install <name>` SHALL resolve a configured link when the name identifies a link and no requested root pack with that ID is installed. Before mutation, LunaPack SHALL resolve the source, resolve the effective Git ref when applicable, evaluate selectors, map safe targets, calculate content digests, and preflight the complete managed-file plan. It SHALL then copy selected files and persist link ownership and provenance atomically. Link files SHALL use the same existing conflict and explicit adoption rules as pack-managed files. LunaPack SHALL reject duplicate link installation.

#### Scenario: Install a configured local link

- **WHEN** a user installs a valid local-source link whose targets pass preflight
- **THEN** LunaPack copies every selected file and records the link and per-file ownership atomically

#### Scenario: Install a configured Git link

- **WHEN** a user installs a valid Git-source link
- **THEN** LunaPack copies files from one resolved commit and records that commit with per-file ownership

#### Scenario: Refuse a conflicting link installation

- **WHEN** a selected target contains unowned content or belongs to another root and no supported explicit adoption applies
- **THEN** LunaPack returns a non-success result without changing files, configuration, or lock state

### Requirement: Update installed links from current selections

`luna update <name>` SHALL update an installed link by resolving its locked configured-source identity, resolving the effective Git ref when applicable, and re-evaluating the current definition. LunaPack SHALL compare normalized selected source paths, mapped targets, and SHA-256 content digests with lock state; classify additions, changes, removals, and uniquely identifiable same-digest moves; apply the resulting managed-file plan through existing ownership, conflict, local-modification, and transaction protections; and replace resolved link state only after success. A local-source link SHALL use content digests rather than timestamps as authoritative evidence. A Git commit change without a selected-file, target, content, or definition change SHALL leave the link current.

#### Scenario: Apply selected-file additions and removals

- **WHEN** a Git or local source changes so one file newly matches and another no longer matches
- **THEN** LunaPack adds and removes the corresponding unchanged managed targets and updates lock state atomically

#### Scenario: Update changed selected content

- **WHEN** a selected source file's SHA-256 digest differs from its locked digest
- **THEN** LunaPack updates its managed target through existing local-modification protections and records the resulting digest

#### Scenario: Detect a unique moved source file

- **WHEN** exactly one removed selected source path and one added selected source path have the same digest and map as one logical file move
- **THEN** LunaPack reports the move and applies its target change through existing ownership protections

#### Scenario: Ignore an unrelated Git commit

- **WHEN** a Git source resolves to a new commit but the link definition, selected source paths, mapped targets, and content digests are unchanged
- **THEN** LunaPack reports the link as current and does not require an update

#### Scenario: Update after a definition change

- **WHEN** an installed link's configured definition differs from its locked definition digest
- **THEN** LunaPack re-evaluates and applies the changed definition through the normal update transaction

### Requirement: Report outdated links

`luna outdated` SHALL evaluate installed links in addition to requested root packs. A link SHALL be outdated when its definition digest changed, a selected file digest changed, a new file matches, a prior file no longer matches, or a mapped target changed. Each outdated link result SHALL identify the link name and the reasons it is outdated. A Git source resolving to a different commit SHALL not by itself make the link outdated.

#### Scenario: Report a newly matching file

- **WHEN** a file added to a source now matches an installed link include and is not excluded
- **THEN** `luna outdated` reports that link with an added-file reason

#### Scenario: Omit a content-equivalent new commit

- **WHEN** an installed Git link resolves to a different commit but its definition and complete selected-file result are unchanged
- **THEN** `luna outdated` does not report that link

### Requirement: Audit installed link ownership

`luna audit` SHALL evaluate every installed link target against its locked effective path and SHA-256 digest and SHALL report missing, locally modified, and ownership-conflicting files. Audit SHALL not mutate project files, configuration, or lock state.

#### Scenario: Report a locally modified linked file

- **WHEN** an installed link target's current digest differs from its locked digest
- **THEN** `luna audit` identifies the link and modified target without changing it

#### Scenario: Report a missing linked file

- **WHEN** an installed link target no longer exists
- **THEN** `luna audit` identifies the link and missing target without recreating it

### Requirement: Uninstall links with digest protection

`luna uninstall <name>` SHALL remove an installed link's definition, unchanged exclusively owned targets, and resolved lock record atomically. If any owned target differs from its recorded digest, LunaPack SHALL preserve every file, the definition, and lock state and SHALL return a non-success result. Uninstalling a link SHALL not affect unrelated packs or links.

#### Scenario: Uninstall an unchanged link

- **WHEN** every target owned by an installed link matches its locked digest
- **THEN** LunaPack removes those targets, the link definition, and its lock record atomically

#### Scenario: Preserve a modified link installation

- **WHEN** any target owned by an installed link differs from its locked digest
- **THEN** LunaPack returns a non-success result and preserves all managed files, configuration, and lock state
