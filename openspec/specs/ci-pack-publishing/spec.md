# ci-pack-publishing Specification

## Purpose

Provide installable GitHub Actions workflow packs that publish changed LunaPack directories without reimplementing Luna's pack identity, artifact, or registry logic.

## Requirements

### Requirement: Provide separate installable registry workflow packs

The repository SHALL provide separate installable packs for generic OCI, ACR,
and GHCR publishing. Each pack SHALL include a changed-pack publishing workflow
and document required source configuration, permissions, and authentication.
The workflows SHALL be delivered as packs, not from an `examples/` directory.

#### Scenario: Install a GHCR publishing workflow

- **WHEN** a project installs the GHCR publishing workflow pack
- **THEN** the workflow and required source configuration guidance are available
  in the project without copying files from an examples directory

#### Scenario: Use generic workflow pack for Harbor

- **WHEN** a project uses the generic OCI workflow pack with a Harbor source
- **THEN** the workflow delegates publication to `luna publish` and needs no
  Harbor-specific Luna integration

### Requirement: Share changed-pack publishing behavior

Each workflow pack SHALL compose the shared PowerShell publisher pack. Its
workflow SHALL pass JSON paths from a SHA-pinned `tj-actions/changed-files`
action to that publisher.

#### Scenario: Install the shared publisher

- **WHEN** a project installs a generic OCI, ACR, or GHCR workflow pack
- **THEN** the shared publisher script is installed under `.github/scripts/`
  and the workflow passes changed paths as JSON

### Requirement: Publish only changed pack folders on main

The sample workflow SHALL run on `main` pushes affecting `packs/**` and publish
each changed direct-child folder once. The shared publisher SHALL publish only
existing folders that contain `pack.yml`. It SHALL support multi-file,
multi-pack, squash, merge, rename, and deleted-pack changes without assuming
`HEAD^`.

#### Scenario: Publish two changed packs once each

- **WHEN** multiple files changed in `packs/backend` and `packs/security`
- **THEN** the workflow invokes `luna publish` exactly once for each folder

#### Scenario: Skip a deleted pack

- **WHEN** a changed pack directory no longer exists at the pushed revision
- **THEN** the workflow does not invoke publication for that directory

#### Scenario: Handle the first push

- **WHEN** the before SHA is the all-zero initial-push sentinel
- **THEN** the publisher scans all existing direct-child folders under `packs/`
  and publishes each folder that contains `pack.yml` once

### Requirement: Install Luna using a documented distribution

The workflow SHALL install Luna through an existing documented installation
method before publishing, using a selected CLI version that supports OCI.
It SHALL use normal project source configuration and SHALL NOT contain ZIP,
OCI manifest/media type, ORAS command, or registry API implementation.

#### Scenario: Publish using only folder and target

- **WHEN** the sample publishes `packs/backend`
- **THEN** it calls `luna publish "packs/backend" --target private` and delegates
  identity, version, packaging, and transport to Luna

### Requirement: Authenticate publishing workflows without Luna credential environment variables

The ACR workflow SHALL support GitHub OIDC/workload federation through Azure
identity. Generic OCI and GHCR workflows SHALL use credentials already stored in
the runner user's native credential store. None SHALL supply registry
credentials through LunaPack-specific environment variables or committed files.

#### Scenario: Publish ACR releases using federation

- **WHEN** the workflow has a configured federated Azure identity with repository
  push permission
- **THEN** Luna publishes noninteractively without an Azure client secret or
  separate Luna authentication command

#### Scenario: Publish GHCR from a prepared self-hosted runner

- **WHEN** the workflow runs on a self-hosted runner whose native credential
  store contains credentials for the configured GHCR source
- **THEN** Luna publishes without reading registry credentials from environment
  variables or prompting

#### Scenario: Publish to a generic registry from a prepared runner

- **WHEN** the workflow runs on a runner whose native credential store contains
  credentials for the configured generic OCI source
- **THEN** Luna publishes without provider-specific Luna environment variables

### Requirement: Surface publishing and change-detection failures

The workflow SHALL fail on change-detection, authentication, or publication
errors. Existing manifest versions SHALL fail clearly rather than being skipped
or overwritten.

#### Scenario: Publish without incrementing the version

- **WHEN** a changed pack's manifest version is already published
- **THEN** Luna rejects it and the job fails with guidance to increment the version

#### Scenario: Fail change detection

- **WHEN** change detection fails
- **THEN** the workflow fails visibly rather than claiming there are no changed packs

### Requirement: Reject changed paths outside pack boundaries

The shared publisher SHALL handle whitespace safely and reject malformed JSON,
absolute paths, and traversal segments before publication.

#### Scenario: Reject invalid changed paths

- **WHEN** the changed-files output contains malformed JSON, an absolute path,
  or a traversal segment
- **THEN** the publisher fails without publishing a path outside `packs/`
