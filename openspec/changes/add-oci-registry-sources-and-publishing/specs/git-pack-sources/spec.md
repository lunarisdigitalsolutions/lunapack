# Spec Delta

## ADDED Requirements

### Requirement: Diagnose Git authentication through existing credentials

Git authentication SHALL continue using Git Credential Manager, credential
helpers, SSH configuration/agent, and supported environment configuration.
Luna SHALL NOT collect or store Git passwords. Authentication failures SHALL
identify the source, recommend its Git credential manager or SSH configuration,
and explain local ignoring as an option.

#### Scenario: Fail a private Git clone

- **WHEN** source `engineering` fails Git access because authentication was rejected
- **THEN** Luna reports `Unable to authenticate to Git source 'engineering'`,
  recommends Git credential-manager/SSH authentication, and mentions
  `luna sources ignore engineering` without prompting for a Git password

### Requirement: Classify Git failures without hiding independent sources

Git authentication, permission, timeout, unavailable executable, and network
failures SHALL remain distinguishable without leaking process secrets.
Optional failed Git sources SHALL not block accessible configured sources.
Required Git sources SHALL still fail the affected operation without mutation.

#### Scenario: Search after a Git authentication failure

- **WHEN** a Git source cannot authenticate but local and OCI sources are accessible
- **THEN** Luna warns for the Git source and retains the other catalog results

#### Scenario: Require an inaccessible locked Git source

- **WHEN** update needs a pack's locked Git source and authentication fails
- **THEN** Luna fails that update with Git authentication guidance and preserves
  files and project state
