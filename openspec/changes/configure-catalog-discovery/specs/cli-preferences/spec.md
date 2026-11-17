# Spec Delta

## Purpose

Provide durable, predictable Luna CLI preferences across project-local,
user-project, and user-global scopes.

## ADDED Requirements

### Requirement: Manage supported CLI preferences

`luna config set <key> <value>`, `luna config list`, and
`luna config remove <key>` SHALL manage the Boolean preferences
`discover.only-composites`, `search.only-composites`, and
`next-steps.enabled`. Listing SHALL show values in the selected scope.
Invalid keys or values SHALL return a non-success result without mutation.

#### Scenario: Set a catalog preference

- **WHEN** a user runs `luna config set discover.only-composites true`
- **THEN** LunaPack stores the Boolean value in the selected scope

#### Scenario: Set next-step guidance preference

- **WHEN** a user runs `luna config set next-steps.enabled false`
- **THEN** LunaPack stores the value without changing other preferences

#### Scenario: List and remove a scoped preference

- **WHEN** a user lists or removes a preference in a selected scope
- **THEN** LunaPack displays or removes only the value stored in that scope

#### Scenario: Reject an unknown key or invalid value

- **WHEN** a user sets an unsupported key or supplies a value other than
  `true` or `false`
- **THEN** LunaPack returns a non-success result without changing any settings

### Requirement: Select CLI preference storage scope

Every `luna config` operation SHALL accept `--project` (with `--local` as an
alias), `--user-project`, and `--global` scope options. Distinct scopes SHALL be
mutually exclusive. `--project` and `--local` SHALL use the current project's
`lunapack.yml`; `--user-project` SHALL use that canonical project directory's
entry in user settings; and `--global` SHALL use user-wide settings. When no
scope is specified, the operation SHALL use the global scope.

#### Scenario: Store a project preference

- **WHEN** a user sets a preference with `--project`
- **THEN** LunaPack stores it in the current project's `lunapack.yml`

#### Scenario: Retain the local scope alias

- **WHEN** a user sets a preference with `--local`
- **THEN** LunaPack stores it in the current project's `lunapack.yml`

#### Scenario: Store a user preference for one project

- **WHEN** a user sets a preference with `--user-project`
- **THEN** LunaPack stores it in user settings under the current canonical
  project directory

#### Scenario: Use global scope by default

- **WHEN** a user sets, lists, or removes a preference without a scope option
- **THEN** LunaPack operates on user-global settings

#### Scenario: Reject conflicting scopes

- **WHEN** a user supplies more than one scope option
- **THEN** LunaPack returns a non-success result without changing settings

### Requirement: Resolve effective CLI preferences

For each preference, LunaPack SHALL use the first configured value in this
order: project-local, user-project, user-global, then built-in default.
Built-in defaults SHALL be `false` for both `only-composites` keys and `true`
for `next-steps.enabled`. An explicit `--only-composites` option SHALL enable
composite-only filtering for that invocation, and `--suppress-next-steps` SHALL
disable guidance for that invocation regardless of the stored preference.

#### Scenario: Use the highest-precedence configured value

- **WHEN** the same preference has different values in project-local,
  user-project, and user-global settings
- **THEN** LunaPack uses the project-local value

#### Scenario: Fall back through preference scopes

- **WHEN** a preference is absent from project-local settings
- **THEN** LunaPack uses the user-project value, then the global value, or the
  built-in default when no scope defines it

#### Scenario: Apply a command-line catalog filter

- **WHEN** a catalog preference is false and the user supplies
  `--only-composites`
- **THEN** that command filters out raw packs for the current invocation

#### Scenario: Suppress enabled next-step guidance for one invocation

- **WHEN** next-step guidance is enabled in settings and the user supplies
  `--suppress-next-steps`
- **THEN** LunaPack omits guidance for that invocation
