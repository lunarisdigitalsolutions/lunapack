# Spec Delta

## ADDED Requirements

### Requirement: Warn about inherited lifecycle script environment

Before an authorized script starts, LunaPack SHALL compare inherited environment variable names with a platform-aware built-in allow-list plus the union of configured additions from project, project-local user, and global-user scopes. It SHALL report names outside the effective list without reporting values, while preserving the inherited child environment during this warning-only rollout.

#### Scenario: Warn about variables outside the effective allow-list

- **WHEN** an authorized script inherits variables not present in the built-in allow-list or any configured scope
- **THEN** LunaPack warns with the variable names before launching the script and leaves the child environment unchanged

#### Scenario: Merge allowed-variable additions across scopes

- **WHEN** a variable name is configured in any one of the project, project-local user, or global-user scopes
- **THEN** LunaPack includes that name in the effective allow-list and does not report it as outside the list

#### Scenario: Do not reveal inherited variable values

- **WHEN** LunaPack warns about an inherited variable outside the effective allow-list
- **THEN** the warning contains its name but not its value

#### Scenario: Do not warn for scripts that will not execute

- **WHEN** a script is denied, skipped, excluded by its condition, or only shown in a dry run
- **THEN** LunaPack emits no environment warning for that script

### Requirement: Enforce lifecycle script timeouts

LunaPack SHALL apply a 300-second default wall-clock limit to each script hook. Configured values SHALL range from 1 through 1,800 seconds, and the smallest explicitly configured value across scopes SHALL take effect. On timeout, LunaPack SHALL terminate the full process tree, fail the hook, and report timeout distinctly from user cancellation.

#### Scenario: Apply the default script timeout

- **WHEN** no project, project-local user, or global-user scope configures a timeout
- **THEN** each executed script hook has a 300-second wall-clock limit

#### Scenario: Apply the strictest configured timeout

- **WHEN** multiple scopes configure valid timeouts
- **THEN** the smallest configured value limits every executed script hook

#### Scenario: Terminate a process tree on timeout

- **WHEN** a script hook exceeds its effective timeout while its process has descendants
- **THEN** LunaPack terminates the script and its descendants, reports a timeout failure, and applies existing lifecycle failure and rollback behavior

#### Scenario: Distinguish timeout from user cancellation

- **WHEN** the caller cancels script execution before the effective timeout expires
- **THEN** LunaPack terminates the full process tree and reports user cancellation rather than timeout
