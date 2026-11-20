# Spec Delta

## ADDED Requirements

### Requirement: Persist lifecycle script policy in supported scopes

Project trust configuration SHALL support optional lifecycle-script settings for allowed environment variable names and timeout seconds. Global-user and canonical local-project user policy SHALL use a dedicated `<profile>/.lunapack/lifecycle-hook-policy.yml` sidecar, separate from `config.yml`. These settings SHALL configure runtime policy only and SHALL NOT grant script execution or override script denial.

#### Scenario: Configure project lifecycle script policy

- **WHEN** `lunapack.yml` contains `trust.scripts.allowedEnvironmentVariables` or `trust.scripts.timeoutSeconds`
- **THEN** LunaPack reads the values as project-scoped lifecycle script policy

#### Scenario: Configure global and local-project user policy

- **WHEN** the sidecar contains global policy or policy keyed by the canonical project directory
- **THEN** LunaPack reads the values in their respective scopes

#### Scenario: Keep policy separate from script authorization

- **WHEN** a scope configures lifecycle script policy without source or pack trust
- **THEN** that policy does not authorize any lifecycle script

#### Scenario: Preserve script denial

- **WHEN** a scope has script denial and also configures lifecycle script policy
- **THEN** script denial continues to prevent execution

### Requirement: Combine lifecycle script policy across scopes

LunaPack SHALL combine project, project-local user, and global-user lifecycle script settings. The effective allowed-variable set SHALL include the built-in platform allow-list and additions from every scope; the effective timeout SHALL be the smallest explicitly configured value, or 300 seconds when none is configured.

#### Scenario: Union allowed-variable additions

- **WHEN** a variable name appears in any one of the three policy scopes
- **THEN** it is included in the effective allowed-variable set

#### Scenario: Use the shortest configured timeout

- **WHEN** more than one scope specifies a valid timeout
- **THEN** LunaPack applies the smallest specified timeout

#### Scenario: Use the default timeout when scopes omit it

- **WHEN** all policy scopes omit `timeoutSeconds`
- **THEN** LunaPack applies a 300-second timeout

### Requirement: Validate lifecycle script policy values

Configured allowed-variable entries SHALL be unique, non-empty environment variable names without `=` or NUL characters. A configured `timeoutSeconds` SHALL be an integer from 1 through 1,800, inclusive. Invalid policy values SHALL make the containing configuration invalid before lifecycle script execution.

#### Scenario: Accept policy values at timeout boundaries

- **WHEN** configuration uses `timeoutSeconds: 1` or `timeoutSeconds: 1800` and valid variable names
- **THEN** lifecycle script policy is valid

#### Scenario: Reject an out-of-range timeout

- **WHEN** a policy configures a timeout below 1 or above 1,800 seconds
- **THEN** LunaPack rejects the containing configuration before executing a script

#### Scenario: Reject invalid or duplicate variable names

- **WHEN** a policy contains an empty name, a name with `=` or NUL, or a duplicate name
- **THEN** LunaPack rejects the containing configuration before executing a script

### Requirement: Preserve legacy user settings compatibility

LunaPack SHALL store user lifecycle-script policy outside `config.yml` so new policy settings do not make that existing user-settings document unreadable to older strict CLI readers. Updating policy SHALL leave `config.yml` unchanged and SHALL atomically preserve unrelated global and project records in the sidecar.

#### Scenario: Save user policy without changing legacy settings

- **WHEN** LunaPack saves global-user or local-project lifecycle-script policy
- **THEN** it writes the sidecar and leaves `config.yml` unchanged

#### Scenario: Preserve unrelated sidecar scopes

- **WHEN** LunaPack updates policy for one scope
- **THEN** it retains global policy and every other canonical project entry
