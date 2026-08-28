# manifest-schemas Delta Specification

## MODIFIED Requirements

### Requirement: Represent named sources and project script trust in version-1 configuration

The `lunapack.yml` schema SHALL require every local, Git, and GitHub-derived source entry to contain a non-empty `name`. Source names SHALL be unique within one project configuration. The schema SHALL allow a project-scoped `trust` mapping containing optional unique source-name entries, optional unique pack entries, and an optional `deny` mapping whose optional `scripts` boolean defaults to `false`. Each pack entry SHALL require a configured source name and bare pack ID without a version selector. Omitted trust, denial, source-trust, and pack-trust properties SHALL represent empty grants with scripts not denied. Existing schema version `1` SHALL be retained.

#### Scenario: Validate named sources and trust

- **WHEN** a version-1 project configuration contains uniquely named sources, distinct trusted source names, and distinct source-plus-pack-ID entries
- **THEN** the project configuration is valid

#### Scenario: Validate project script denial alone

- **WHEN** a version-1 project configuration contains `trust.deny.scripts: true` without source or pack trust collections
- **THEN** the project configuration is valid and declares portable script denial

#### Scenario: Default omitted project denial off

- **WHEN** a version-1 project configuration omits `trust`, `trust.deny`, or `trust.deny.scripts`
- **THEN** the configuration is valid and does not deny scripts

#### Scenario: Reject version-specific pack trust

- **WHEN** a trusted pack entry contains an `@version` selector
- **THEN** the project configuration is invalid

#### Scenario: Reject duplicate source names

- **WHEN** two configured sources have the same ordinal name
- **THEN** the project configuration is invalid

#### Scenario: Reject pack trust without a source

- **WHEN** a trusted pack entry contains an ID but no configured source name
- **THEN** the project configuration is invalid

#### Scenario: Preserve empty trust collections

- **WHEN** a version-1 project configuration contains empty `trust.sources` and `trust.packs` collections with omitted denial
- **THEN** the project configuration is valid without a schema-version migration and does not deny scripts

### Requirement: Define cross-platform user trust settings

LunaPack SHALL define a user-settings document at `~/.lunapack/config.yml`. It SHALL contain optional global source and source-plus-pack-ID trust entries, optional global `deny.scripts` policy, and optional local-project trust records keyed by canonical absolute project directory. A local-project record SHALL support the same grants and denial policy and MAY acknowledge project-scoped source and pack declarations by their exact source identities. Acknowledgements SHALL contain only positive source and pack entries and SHALL not contain denial policy. Omitted denial SHALL default to `false`; omitted source and pack collections SHALL default to empty. Duplicate, incomplete, version-qualified, or unsafe project-path entries SHALL be invalid.

#### Scenario: Validate global and local-user trust

- **WHEN** user settings contain global trust and a local-project record keyed by a canonical project path
- **THEN** the settings are valid on the current operating system

#### Scenario: Validate user denial without grants

- **WHEN** global-user or project-local user settings contain `deny.scripts: true` without source or pack collections
- **THEN** the settings are valid and deny scripts in that scope

#### Scenario: Default omitted user denial off

- **WHEN** global-user or project-local user settings omit `deny.scripts`
- **THEN** scripts are not denied by that scope

#### Scenario: Reject denial in project acknowledgements

- **WHEN** a project acknowledgement contains a script-denial policy
- **THEN** the user settings are invalid

#### Scenario: Reject a relative local-project key

- **WHEN** a local-project trust record uses a relative project path
- **THEN** the user settings are invalid
