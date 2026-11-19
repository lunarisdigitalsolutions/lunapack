# Spec Delta

## Purpose

Provide agents with portable, accurate, permission-aware procedures for using
Luna CLI across supported coding-agent harnesses.

## ADDED Requirements

### Requirement: Guide pack discovery and installation

The `luna-cli` skill SHALL guide agents to check configured source identities,
find candidate packs, inspect exact releases, validate them, and preview
installation with lifecycle scripts disabled. It SHALL use supported Luna
commands and provide runnable examples.

#### Scenario: Evaluate a pack before installation

- **WHEN** an agent is asked to find and install a pack
- **THEN** it checks configured sources, uses `luna discover` or `luna search`,
  inspects and validates an exact release, then previews `luna install` with
  `--dry-run --scripts skip` before changing the project

### Requirement: Guide source setup and pack authoring

The `luna-cli` skill SHALL guide source setup and pack manifest creation or
editing with existing `luna sources` and `luna pack` commands. It SHALL direct
authors to validate each candidate manifest with `luna pack validate`.

#### Scenario: Create and validate a pack

- **WHEN** an agent is asked to create or edit a pack
- **THEN** it uses supported `luna pack init`, `add`, `set`, or `rm` commands
  as appropriate, then runs `luna pack validate` before presenting the change

### Requirement: Guide pack maintenance

The `luna-cli` skill SHALL match maintenance guidance to the user's request and
use supported commands for outdated checks, updates, audits, and uninstall.
It SHALL distinguish read-only inspection from project-changing commands.

#### Scenario: Inspect installed packs

- **WHEN** an agent is asked to review installed packs without changing them
- **THEN** it uses read-only commands such as `luna outdated` or `luna audit`
  and does not run update or uninstall

### Requirement: Distribute one canonical skill

The skill SHALL use stable identity `luna-cli` for both skill metadata and
LunaPack pack ID. Direct Skills CLI and LunaPack project installation SHALL use
the same canonical skill content and target `.agents/skills/luna-cli`.

#### Scenario: Install through either route

- **WHEN** a user installs the skill directly with `npx skills` or through its
  LunaPack release
- **THEN** the installed project skill has identity `luna-cli` and uses the
  canonical skill content at `.agents/skills/luna-cli`

### Requirement: Keep project ownership unambiguous

Installation guidance SHALL assign each project skill copy to one installer.
Before switching routes, users SHALL remove the copy through its current
installer. The LunaPack release SHALL fail on an existing target rather than
overwrite or relocate it.

#### Scenario: Switch installation routes

- **WHEN** a project already has a `luna-cli` copy managed by one installer
- **THEN** guidance requires removal through that installer before using the
  other route, and LunaPack does not overwrite the existing target

### Requirement: Preserve human approval boundaries

The skill SHALL require explicit user authorization for project mutations,
source additions, persistent trust changes, and lifecycle script execution.
For an install request, it SHALL preview changes with scripts disabled before
writing unless the user has already authorized that exact operation.

#### Scenario: Preview before an unapproved install

- **WHEN** an agent identifies a pack to install but lacks authorization to
  change project files
- **THEN** it presents a dry-run using `--scripts skip` and waits for approval
  before running the mutating install

#### Scenario: Avoid implicit script trust

- **WHEN** a pack declares lifecycle scripts or proposes external sources
- **THEN** the agent reviews the proposed effects and asks for explicit
  approval before accepting sources, granting trust, or executing scripts
