# CLI Environment Preflight Specification

## Purpose

Help Luna users identify environment and configured-source problems before an operation fails, while preserving project state and keeping remote access opt-in.

## Requirements

### Requirement: Run local preflight checks without project mutation

`luna doctor` SHALL inspect the selected project's state and applicable user environment without changing project configuration, lock state, user settings, or credentials. Checks SHALL be offline by default.

#### Scenario: Run the default preflight

- **WHEN** a user runs `luna doctor` without network options
- **THEN** Luna reports local environment and state checks without contacting configured remote sources

#### Scenario: Diagnose invalid state

- **WHEN** selected project or user state is missing, unreadable, or invalid
- **THEN** Luna reports the affected state and remediation instead of stopping during preference resolution or creating replacement state

### Requirement: Report actionable check status and project blockers

Each check SHALL report pass, fail, or informational status with an explanation and useful remediation. Luna SHALL return a non-zero exit code only when a failure prevents an operation configured for the selected project.

#### Scenario: Report a project-relevant blocker

- **WHEN** a failed check prevents an operation configured for the selected project
- **THEN** `luna doctor` reports the failure and remediation and returns a non-zero exit code

#### Scenario: Report an unrelated environment problem

- **WHEN** an environment problem does not prevent any operation configured for the selected project
- **THEN** `luna doctor` reports it as informational and returns zero if no other blocking failure exists

### Requirement: Check profile and cache directory writability safely

For each existing profile or cache directory required by configured operations, Luna SHALL test writability with a uniquely named temporary file and remove the file after the check. Luna SHALL NOT create a missing directory or write project state. A failed probe or cleanup SHALL be reported.

#### Scenario: Remove a successful write probe

- **WHEN** Luna successfully creates a temporary probe in an existing required directory
- **THEN** Luna removes the probe and leaves project state unchanged

#### Scenario: Report an unavailable directory

- **WHEN** a required profile or cache directory is missing or cannot be probed or cleaned up
- **THEN** Luna reports the condition and remediation without creating the directory or leaving a probe file

### Requirement: Check configured remote sources only when requested

`luna doctor --network` SHALL check configured Git and OCI sources in the selected project using bounded timeouts. It SHALL use only existing credentials, SHALL NOT prompt or save credentials, and SHALL NOT expose secret values. Local sources SHALL not cause network requests.

#### Scenario: Check configured Git and OCI sources

- **WHEN** a user runs `luna doctor --network` for a project with configured Git or OCI sources
- **THEN** Luna reports a bounded access result for each such source without changing project state or credentials

#### Scenario: Skip network checks without the option

- **WHEN** a project has configured remote sources and a user runs `luna doctor` without `--network`
- **THEN** Luna makes no network request for those sources

#### Scenario: Bound a stalled network check

- **WHEN** a configured Git or OCI source does not respond before the check timeout
- **THEN** Luna stops that check, reports a timeout, and continues reporting other checks

#### Scenario: Avoid network access for local sources

- **WHEN** a project has only local sources and a user runs `luna doctor --network`
- **THEN** Luna reports that no remote source checks are needed and contacts no remote host
