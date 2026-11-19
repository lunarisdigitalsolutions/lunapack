# Spec Delta

## Purpose

Scoped CLI preferences let users configure security-sensitive defaults at
project, user-project, and global scopes with predictable precedence.

## ADDED Requirements

### Requirement: Configure insecure Git transport by scope

The CLI SHALL expose `git.allow-insecure-transport` as a Boolean preference
through `luna config` and persist it in the selected project, user-project, or
global settings scope. This setting SHALL apply to every insecure Git source in
the selected scope and SHALL NOT be source-specific.

#### Scenario: Set the preference in each scope

- **WHEN** a user configures `git.allow-insecure-transport` in a supported scope
- **THEN** the CLI persists and reports the Boolean value for that scope

### Requirement: Insecure Git transport is disabled by default

The CLI SHALL treat `git.allow-insecure-transport` as `false` when no scope
configures a value.

#### Scenario: No scope configures the preference

- **WHEN** a Git network operation targets an insecure transport and no scope
  configures the preference
- **THEN** the effective preference is `false`

### Requirement: Resolve insecure Git preference by scope precedence

The CLI SHALL resolve `git.allow-insecure-transport` from project, then
user-project, then global scope, using the first explicitly configured value.

#### Scenario: More specific value overrides broader value

- **WHEN** a more specific scope configures `false` and a broader scope
  configures `true`
- **THEN** the effective preference is `false`

#### Scenario: Broader value applies when specific scopes omit preference

- **WHEN** project and user-project scopes omit the preference and global scope
  configures `true`
- **THEN** the effective preference is `true`
