## ADDED Requirements

### Requirement: Guide pack instance selection

LunaPack SHALL identify pack instances by pack ID and alias in lifecycle output. When a named update or uninstall is ambiguous, guidance SHALL list the matching aliases and provide complete `--name` commands for selecting them. Successful named install, update, rename, and uninstall guidance SHALL include the affected alias and SHALL not suggest an operation against a sibling instance unless that operation is independently relevant.

#### Scenario: Guide an ambiguous update

- **WHEN** `orders` and `customers` instances of `dotnet-api` exist without a default and a user runs `luna update dotnet-api`
- **THEN** guidance lists both aliases and includes complete update commands using `--name orders` and `--name customers`

#### Scenario: Identify a successful named installation

- **WHEN** installation of `dotnet-api --name orders` succeeds
- **THEN** the result and next-step guidance identify the installed instance as `dotnet-api` with alias `orders`

#### Scenario: Preserve sibling guidance after uninstall

- **WHEN** uninstalling one named instance leaves another instance of the same pack installed
- **THEN** guidance identifies the removed alias and does not suggest cleanup that would invalidate the remaining instance
