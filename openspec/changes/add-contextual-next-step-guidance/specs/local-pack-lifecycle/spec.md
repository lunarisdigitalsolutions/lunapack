## ADDED Requirements

### Requirement: Guide successful pack lifecycle transitions

Completed non-dry-run lifecycle commands SHALL confirm their result and append
recommendations selected from persisted post-operation state. Guidance SHALL
use a concrete pack ID when the completed command supplies one.

#### Scenario: Install a pack

- **WHEN** `luna install <pack-reference>` successfully installs a requested
  root pack
- **THEN** Luna confirms the installed pack ID and recommends `luna outdated`,
  `luna update`, and uninstalling that pack ID

#### Scenario: Update installed packs

- **WHEN** `luna update` successfully completes one or more updates
- **THEN** Luna reports the updated requested-root count and recommends `luna
  audit` and `luna outdated`

#### Scenario: Uninstall a pack while others remain

- **WHEN** `luna uninstall <pack-id>` succeeds and at least one requested root
  remains installed
- **THEN** Luna confirms the uninstalled pack ID and recommends `luna discover`
  and `luna install <pack>`

#### Scenario: Uninstall the last pack

- **WHEN** `luna uninstall <pack-id>` succeeds and no requested root remains
  installed
- **THEN** Luna confirms the uninstalled pack ID, reports that no packs are
  installed, and recommends `luna discover` and `luna search <keyword>`

### Requirement: Guide recovery from an unresolved installation

When installation cannot resolve a syntactically valid pack reference from
configured sources, Luna SHALL preserve its transactional non-success behavior
and append commands that help locate an available pack.

#### Scenario: Install an unknown pack

- **WHEN** a user runs `luna install unknown-pack` and no configured source
  provides that pack
- **THEN** Luna reports that `unknown-pack` was not found, recommends `luna
  search unknown-pack` followed by `luna discover`, and leaves project files and
  state unchanged
