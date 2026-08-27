## ADDED Requirements

### Requirement: Process ordered typed lifecycle hooks

LunaPack SHALL include every applicable typed hook from directly requested and transient packs in the lifecycle plan. It SHALL authorize all `script` hooks before processing any hook, preserve existing script trust and execution requirements, process pack events in the existing stable dependency-first lifecycle order, and process each event's hooks in manifest order. Composite-reference `disabledHooks` SHALL suppress every script and instruction hook for each named event. Incoming update releases and dependencies newly introduced by an update SHALL retain the existing event-selection rules.

#### Scenario: Process mixed hooks in declared order

- **WHEN** one `preInstall` event declares an instruction, a script, and another instruction in that order
- **THEN** LunaPack displays, executes, and displays those hooks in the declared order after all applicable scripts have been authorized

#### Scenario: Suppress every typed hook for an event

- **WHEN** a composite reference disables `postInstall` for a transient pack whose event contains script and instruction hooks
- **THEN** LunaPack processes none of that pack's `postInstall` hooks

#### Scenario: Preserve script trust enforcement

- **WHEN** an ordered hook list contains an untrusted script and an instruction
- **THEN** LunaPack requires script authorization before displaying the instruction or changing project files or state

### Requirement: Allow consumers to skip instruction hooks

`luna install` and every form of `luna update` SHALL accept `--skip-instructions`. When present, LunaPack SHALL not load, render, parse, display, or prompt for any instruction hook in that invocation. The option SHALL not suppress script hooks or change `--scripts <prompt|run|skip>` behavior. Without `--skip-instructions`, applicable instruction hooks SHALL be processed.

#### Scenario: Skip instructions without skipping scripts

- **WHEN** a user runs install or update with `--skip-instructions` for a pack containing script and instruction hooks
- **THEN** LunaPack processes script hooks under the selected script-consent mode and processes no instruction hooks

#### Scenario: Display instructions by default

- **WHEN** a user runs install or update without `--skip-instructions`
- **THEN** LunaPack processes every applicable non-suppressed instruction hook

### Requirement: Validate and summarize instruction hooks during dry run

With `--dry-run`, LunaPack SHALL load, render, and parse applicable instruction hooks so invalid content fails planning. It SHALL list each planned instruction hook with its pack ID, lifecycle event, file, templating state, and detected step count, but SHALL not enter guided step display or request instruction confirmation.

#### Scenario: Preview a valid instruction hook

- **WHEN** a user runs install or update with `--dry-run` and an applicable instruction hook is valid
- **THEN** LunaPack reports the hook and detected step count without displaying its guided flow or prompting

#### Scenario: Reject an invalid instruction during dry run

- **WHEN** a dry run encounters a missing instruction file or invalid enabled template
- **THEN** LunaPack returns a non-success result without executing hooks or changing project files or state
