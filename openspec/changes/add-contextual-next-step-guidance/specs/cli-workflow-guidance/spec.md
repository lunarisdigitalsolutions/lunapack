## Purpose

Define workspace-aware CLI recommendations that guide users through Luna's core
setup, catalog, installation, and maintenance workflow.

## ADDED Requirements

### Requirement: Summarize workspace maturity from the root command

An empty `luna` invocation SHALL inspect the effective workspace and report its
current maturity stage. A valid workspace summary SHALL count configured
sources and installed requested root packs. The root command SHALL recommend
only actions meaningful for that stage.

#### Scenario: Start without a workspace

- **WHEN** a user runs `luna` where neither `lunapack.yml` nor
  `lunapack-lock.yml` exists
- **THEN** Luna reports that no LunaPack workspace was found, recommends `luna
init`, and states that initialization creates both project-state files

#### Scenario: Open a workspace without sources

- **WHEN** a user runs `luna` in a valid workspace with no configured sources
- **THEN** Luna reports the workspace and missing sources and recommends `luna
sources add git <name> <repository-url>`

#### Scenario: Open a workspace ready for discovery

- **WHEN** a user runs `luna` in a valid workspace with at least one source and
  no installed requested root packs
- **THEN** Luna reports the source and installed-pack counts and recommends
  `luna discover`, `luna search <keyword>`, and `luna install <pack>`

#### Scenario: Open an active workspace

- **WHEN** a user runs `luna` in a valid workspace with at least one installed
  requested root pack
- **THEN** Luna reports the source and installed-pack counts and recommends
  `luna outdated`, `luna update`, and `luna audit`

#### Scenario: Open an invalid workspace

- **WHEN** a user runs `luna` where project-state files are partial or invalid
- **THEN** Luna returns a non-success result describing the invalid state
  without classifying it as an initialized workspace

### Requirement: Render bounded actionable recommendations

Every guidance block SHALL contain at most three actions in relevance order.
Each action SHALL contain a short label and one syntactically complete `luna`
command. The command SHALL use known values when available and SHALL mark an
unknown user-supplied value with an angle-bracket token. Guidance SHALL contain
no documentation links and SHALL follow the command's primary output.

#### Scenario: Render several next actions

- **WHEN** a successful command has three relevant follow-up actions
- **THEN** Luna renders one numbered guidance block containing those three
  labels and commands in advisor order

#### Scenario: Render one next action

- **WHEN** the current context has one relevant follow-up action
- **THEN** Luna renders a singular guidance heading followed by that action and
  command

#### Scenario: Limit excess actions

- **WHEN** more than three actions are available to the advisor
- **THEN** Luna renders only the three highest-priority actions

### Requirement: Preserve command semantics while adding guidance

Guidance SHALL NOT execute commands, prompt for action selection, mutate
workspace state, or change the primary operation's exit status. Luna SHALL emit
command-specific guidance only after completed state-changing operations,
successful catalog output, and recognized recoverable failures. It SHALL NOT
emit state-changing success guidance for a dry run.

#### Scenario: Complete a successful operation

- **WHEN** a command completes successfully and guidance rendering also
  succeeds
- **THEN** Luna returns the command's successful exit status after rendering
  its recommendations

#### Scenario: Preview a lifecycle operation

- **WHEN** a user completes `luna install --dry-run` or `luna update --dry-run`
- **THEN** Luna reports the preview without implying that workspace maturity
  advanced

#### Scenario: Reject invalid command syntax

- **WHEN** System.CommandLine rejects invalid command syntax
- **THEN** Luna retains generated parse-error behavior without appending
  workflow recommendations

### Requirement: Guide recovery from missing workflow prerequisites

Core catalog and lifecycle commands SHALL append one actionable recovery command
when their primary failure is a missing workspace or missing configured source.
The primary error SHALL remain visible and the command SHALL remain
non-successful.

#### Scenario: Run a workflow command without a workspace

- **WHEN** a user runs a core catalog or lifecycle command where neither
  project-state file exists
- **THEN** Luna reports the missing workspace and recommends `luna init`

#### Scenario: Run a source-dependent command without sources

- **WHEN** a user runs a catalog or installation command in a valid workspace
  with no configured sources
- **THEN** Luna reports that no sources are configured and recommends `luna
sources add git <name> <repository-url>`
