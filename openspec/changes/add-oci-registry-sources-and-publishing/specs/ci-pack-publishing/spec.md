# Spec Delta

## Purpose

Show pack authors how to publish changed private-pack directories in GitHub
Actions without reimplementing Luna's pack identity, artifact, or registry logic.

## ADDED Requirements

### Requirement: Provide a configured private-pack repository example

Documentation SHALL show a valid `lunapack.yml` OCI target and independent
`packs/<folder>/pack.yml` directories with files/scripts. It SHALL include
ACR, GHCR, Harbor, and generic registry configurations, source paths, scoped
permissions, and nonsecret configuration. Planned examples SHALL be identified
as planned until the feature ships.

#### Scenario: Configure an example private source

- **WHEN** an author follows the example configuration
- **THEN** `private` selects an OCI registry/path without embedding tokens or
  requiring the workflow to know each pack's ID/version

### Requirement: Publish only changed pack folders on main

The sample workflow SHALL run on `main` pushes affecting `packs/**`, compare
`github.event.before` with `github.sha`, and publish each changed direct child
folder once. It SHALL support multi-file, multi-pack, squash, merge, rename,
and deleted-pack changes without assuming `HEAD^`.

#### Scenario: Publish two changed packs once each

- **WHEN** multiple files changed in `packs/backend` and `packs/security`
- **THEN** the workflow invokes `luna publish` exactly once for each folder

#### Scenario: Skip a deleted pack

- **WHEN** a changed pack directory no longer exists at the pushed revision
- **THEN** the workflow does not invoke publication for that directory

#### Scenario: Handle the first push

- **WHEN** the before SHA is absent or the all-zero initial-push sentinel
- **THEN** the workflow compares the pushed tree with an empty tree rather than
  failing or assuming a parent commit

### Requirement: Install Luna using a documented distribution

The workflow SHALL install Luna through an existing documented installation
method before publishing, using a selected CLI version that supports OCI.
It SHALL use normal project source configuration and SHALL NOT contain ZIP,
OCI manifest/media type, ORAS command, or registry API implementation.

#### Scenario: Publish using only folder and target

- **WHEN** the sample publishes `packs/backend`
- **THEN** it calls `luna publish "packs/backend" --target private` and delegates
  identity, version, packaging, and transport to Luna

### Requirement: Authenticate CI without permanent Azure secrets

The ACR sample SHALL prefer GitHub OIDC/workload federation with
`contents: read` and `id-token: write`, document the identity and registry role
setup, and use Luna's Azure provider. GHCR/generic examples SHALL use
source-bound token/password providers and minimum required permissions.
Secrets SHALL not be printed or committed.

#### Scenario: Publish ACR releases using federation

- **WHEN** the workflow has a configured federated Azure identity with repository
  push permission
- **THEN** Luna publishes noninteractively without an Azure client secret or
  separate Luna authentication command

### Requirement: Surface publishing and change-detection failures

The workflow SHALL fail on change-detection, authentication, or publication
errors. Existing manifest versions SHALL fail clearly rather than being skipped
or overwritten. Folder paths SHALL be handled safely, including whitespace.

#### Scenario: Publish without incrementing the version

- **WHEN** a changed pack's manifest version is already published
- **THEN** Luna rejects it and the job fails with guidance to increment the version

#### Scenario: Fail the revision comparison

- **WHEN** the previous revision cannot be fetched or compared
- **THEN** the workflow fails visibly rather than claiming there are no changed packs
