# Spec Delta

## Purpose

Let each user exclude configured sources for one project without altering shared
configuration, installed provenance, or other users' source access.

## ADDED Requirements

### Requirement: Ignore and restore sources locally

`luna sources ignore <name>` and `luna sources unignore <name>` SHALL change
only the current user's state for the canonical current project. State SHALL
remain outside the repository and SHALL NOT be written to `lunapack.yml` or
`lunapack-lock.yml`. Unknown names SHALL fail without mutation; repeated
ignore/unignore operations SHALL be idempotent.

#### Scenario: Ignore a private source

- **WHEN** a user runs `luna sources ignore company`
- **THEN** only that user's current project excludes `company`, and shared
  configuration, lock state, and managed files remain byte-for-byte unchanged

#### Scenario: Restore a source

- **WHEN** the user runs `luna sources unignore company`
- **THEN** `company` participates in future source access without changing trust
  grants or forcing authentication at unignore time

### Requirement: Apply ignoring consistently across source types

Ignored local, Git, GitHub-shorthand, and OCI sources SHALL not participate in
discovery, candidate resolution, link/external-source access, or publishing.
Luna SHALL not read their source directories, contact their remotes, open
credential providers for them, or prompt for their authentication.

#### Scenario: Browse with an ignored OCI source

- **WHEN** catalog browsing runs with `company` ignored
- **THEN** it makes no registry or credential request for that source

#### Scenario: Resolve content from an ignored Git source

- **WHEN** a pack or link explicitly needs an ignored Git source
- **THEN** Luna reports the ignored-source conflict without invoking Git

### Requirement: Keep ignored sources visible

Source listing and inspection SHALL retain ignored sources, show their normal
name/type/location information in configured order, and mark them
`ignored locally`. Inspecting configuration SHALL not contact those sources.

#### Scenario: List local ignore state

- **WHEN** `company` is ignored and the user runs `luna sources list`
- **THEN** the result includes `company`, `oci`, its registry/path, and
  `ignored locally`

### Requirement: Respect locked and explicit source requirements

When a lock, dependency, link, or publish target requires an ignored source,
Luna SHALL fail with the affected pack/content and source name and guidance to
run `luna sources unignore <name>`. It SHALL NOT substitute another source
silently or change project state.

#### Scenario: Update a pack locked to an ignored source

- **WHEN** `dotnet-project` is locked to ignored source `company`
- **THEN** Luna explains the conflict and how to unignore `company` without
  selecting an identically named pack from another source

#### Scenario: Audit without source access

- **WHEN** an audit or safe uninstall needs only existing managed-file and lock
  evidence for an ignored source
- **THEN** that local-only operation remains available without contacting it

### Requirement: Bind ignore state to exact project and source identity

Local ignore state SHALL not cross user or canonical-project boundaries.
Source rename SHALL preserve ignore state for the unchanged source identity;
changing its endpoint/path or removing and recreating a different source under
the same name SHALL not inherit unrelated state. Unrelated user trust and
settings SHALL be preserved.

#### Scenario: Rename an ignored source

- **WHEN** `company` is renamed to `engineering` without changing its fingerprint
- **THEN** that user's project still ignores the same source under its new name

#### Scenario: Keep another project independent

- **WHEN** another project also configures a source named `company`
- **THEN** it is not ignored because the first project ignored that name
