## ADDED Requirements

### Requirement: Guide successful workspace setup

Successful initialization and source registration SHALL confirm the completed
operation and append the next core setup actions. Guidance SHALL reflect
persisted state after the operation.

#### Scenario: Initialize a workspace

- **WHEN** `luna init` successfully creates project state
- **THEN** Luna confirms initialization and recommends `luna sources add git
<name> <repository-url>` followed by `luna sources list`

#### Scenario: Add a source

- **WHEN** any `luna sources add` variant successfully persists a source
- **THEN** Luna confirms the named source and recommends `luna discover`, `luna
search <keyword>`, and `luna install <pack>`

### Requirement: Remove a configured source safely

`luna sources remove <name>` SHALL remove the source with that exact
case-sensitive name. In the same atomic project-state update, Luna SHALL remove
project-scoped source and pack trust entries bound to that source name. It SHALL
retain requested packs, resolved lock records, managed files, and trust entries
for all other sources.

#### Scenario: Remove a source while others remain

- **WHEN** a user removes a configured source and at least one configured source
  remains
- **THEN** Luna persists the source and associated trust removal, confirms
  success, and recommends `luna sources list` and `luna discover`

#### Scenario: Remove the last source

- **WHEN** a user removes the only configured source
- **THEN** Luna persists the source and associated trust removal, reports that
  no sources remain, and recommends `luna sources add git <name>
<repository-url>`

#### Scenario: Remove a source used by an installed pack

- **WHEN** the removed source is named by an installed pack's resolved lock
  record
- **THEN** Luna retains the requested pack, managed files, and immutable lock
  evidence while removing the configured source and associated trust

#### Scenario: Reject an unknown source name

- **WHEN** a user runs `luna sources remove <name>` for a name that is not
  configured
- **THEN** Luna returns a non-success result without changing configuration,
  trust, lock state, or managed files
