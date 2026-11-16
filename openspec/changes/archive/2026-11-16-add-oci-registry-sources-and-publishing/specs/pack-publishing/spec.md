# Spec Delta

## Purpose

Publish validated LunaPack authoring directories through configured source
targets without duplicating pack identity or registry packaging in user workflows.

## ADDED Requirements

### Requirement: Publish a directory through a named source

Luna SHALL provide `luna publish <folder> --target <source>`. The target SHALL
identify a configured source and determine publishing behavior. Missing,
unknown, ignored, or nonpublishable targets SHALL fail clearly. The command
SHALL NOT require or accept alternate pack identity via `--type`, `--id`, or
`--version`.

#### Scenario: Select an OCI publishing target

- **WHEN** the user runs `luna publish ./packs/dotnet-project --target company`
  and `company` is an enabled OCI source
- **THEN** Luna uses that source's registry and path as the destination

#### Scenario: Reject an unsupported target

- **WHEN** publication targets a local or Git source without a publishing capability
- **THEN** Luna identifies the source and unsupported operation before uploading

### Requirement: Derive ID and version from the pack manifest

Publication SHALL read the selected folder's root `pack.yml`, derive ID and
version solely from it, and validate the complete pack using existing authoring
validation. Missing/invalid manifests, unsafe content, or unrepresentable
identity SHALL fail before upload. Validation SHALL not install the pack,
execute its hooks, or mutate consuming-project state.

#### Scenario: Publish without duplicate identity arguments

- **WHEN** root `pack.yml` declares `id: dotnet-project` and `version: 1.4.0`
- **THEN** Luna publishes that exact ID/version regardless of the folder's name

#### Scenario: Reject an invalid pack

- **WHEN** the manifest or referenced content fails normal pack validation
- **THEN** Luna reports the validation failure without uploading a release

### Requirement: Produce the LunaPack OCI release representation

OCI publishing SHALL preserve the complete pack hierarchy in one ZIP layer,
publish compatible LunaPack metadata and manifest, and tag the artifact with
the manifest version in that pack's source-scoped repository. Success SHALL
identify pack ID/version, target source, complete reference, and resolved digest.

#### Scenario: Publish into a configured source path

- **WHEN** `company` uses `lunapack.azurecr.io` and path `engineering`, and
  `dotnet-project` declares `1.4.0`
- **THEN** Luna reports publication to
  `lunapack.azurecr.io/engineering/dotnet-project:1.4.0` after verifying completion

### Requirement: Reject existing versions without overwriting

Normal publication SHALL fail if the target version tag already exists,
including an incompatible artifact or an identical prior release. Luna SHALL
not silently move version tags or offer an overwrite shortcut. It SHALL
explain that releases are immutable and instruct the author to increment the
manifest version.

#### Scenario: Republish unchanged version

- **WHEN** `dotnet-project:1.4.0` already exists in the target repository
- **THEN** publication returns nonzero and leaves the existing tag/digest unchanged

#### Scenario: Encounter an existing incompatible tag

- **WHEN** another artifact already occupies the requested version tag
- **THEN** Luna rejects publication rather than replacing that artifact

### Requirement: Reuse demand-driven source authentication

Publishing SHALL use the same source-bound authentication and OS credential
storage behavior as reading. An interactive successful credential prompt SHALL
resume publication; CI SHALL use noninteractive providers and never prompt.
Read permission alone SHALL not be treated as successful push authorization.

#### Scenario: Authenticate during publication

- **WHEN** an interactive push needs credentials and its source has none
- **THEN** Luna securely prompts, validates and saves usable credentials, and
  continues the original publishing invocation

### Requirement: Report publication completion and failure honestly

Publication SHALL report success only after the manifest/tag resolves to the
published digest. Upload, authentication, cancellation, race, and registry
errors SHALL be explicit. Temporary archives SHALL be cleaned on every exit.
Publication SHALL not change installed project state or claim success after a
partial blob upload.

#### Scenario: Fail after uploading blobs

- **WHEN** manifest publication fails after content blobs were uploaded
- **THEN** Luna reports failure, cleans local temporary files, and does not
  report a published release or attempt to delete shared registry blobs
