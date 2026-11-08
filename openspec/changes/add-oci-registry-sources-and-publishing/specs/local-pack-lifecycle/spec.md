# Spec Delta

## ADDED Requirements

### Requirement: Consume OCI packs through the existing lifecycle

Validated OCI content SHALL use the normal pack installation/update pipeline,
including managed-file strategies, parameters, templates, references, remapping,
ownership, atomic project-state updates, and script trust/denial. OCI transport
SHALL NOT bypass validation or execute scripts during discovery, inspection,
download, extraction, or publication.

#### Scenario: Install an OCI pack normally

- **WHEN** `luna install dotnet-project` selects a compatible OCI release
- **THEN** Luna validates its extracted directory and applies the normal lifecycle,
  persisting normal ownership and OCI source provenance

#### Scenario: Deny untrusted OCI scripts

- **WHEN** an OCI pack contains lifecycle scripts without effective trust
- **THEN** the existing trust decision remains required before script execution

### Requirement: Preview OCI changes without project mutation

OCI dry runs SHALL resolve, authenticate if needed, retrieve, and validate
current content to produce the normal plan without changing managed files,
project configuration, lock state, or executing scripts. Successful interactive
authentication SHALL still follow secure credential-persistence behavior.

#### Scenario: Dry-run a private pack

- **WHEN** a user dry-runs installation of an accessible private OCI pack
- **THEN** Luna reports its normal managed-file/lifecycle plan without modifying
  project state or executing its hooks

### Requirement: Detect and apply OCI updates with exact provenance

Online outdated/update operations SHALL query OCI versions and preserve normal
semantic-version and locked-source behavior. Updates SHALL materialize the
selected manifest digest consistently. Digest drift at an already locked
version SHALL be reported as an integrity/immutability violation, not silently
installed as the same release.

#### Scenario: Update to a second release

- **WHEN** installed `backend` version `1.0.0` has accessible OCI version `1.1.0`
- **THEN** outdated reports it and update applies `1.1.0` through the normal
  lifecycle while recording the new digest

#### Scenario: Detect a moved version tag

- **WHEN** an online check sees a locked version tag resolve to a different digest
- **THEN** Luna reports changed immutable release evidence without silently
  replacing locked content

### Requirement: Report OCI offline limits without false freshness

Offline operations SHALL not contact OCI registries or create a persistent OCI
cache. Audit and safe uninstall SHALL use existing local ownership/lock evidence
where sufficient. An offline update check SHALL identify OCI releases as
unchecked rather than asserting no updates are available; operations needing
unavailable remote content SHALL fail clearly.

#### Scenario: Check updates offline

- **WHEN** `luna outdated --offline` includes installed OCI packs
- **THEN** Luna reports their remote version state as unchecked without querying
  a registry or declaring them current

### Requirement: Preserve project state after required-source failure

Authentication, ignore conflicts, integrity errors, or availability failure in
a required OCI root, dependency, or external source SHALL fail the affected
operation before mutation. Optional-source failures SHALL not block unrelated
resolvable operations. Existing transactional semantics SHALL apply to the
whole requested graph.

#### Scenario: Fail a required private dependency

- **WHEN** a root is accessible but its required dependency's locked source cannot
  authenticate
- **THEN** installation/update returns nonzero and preserves all previous managed
  files, configuration, and lock records

## MODIFIED Requirements

### Requirement: Resolve composite pack references from configured sources

LunaPack SHALL recursively resolve every composite pack reference from the local, Git, and OCI sources configured in the consuming project's `lunapack.yml`. Each composite reference SHALL resolve the declared ID and exact version using the same source-precedence rules as direct installation. A reference MAY declare a parameter condition. After graph-wide parameters resolve, LunaPack SHALL omit false references and descendants reachable only through them from managed files, hooks, ownership, lock state, and external-source requirements. Pack-local aliases SHALL not be inherited or used for pack discovery.

#### Scenario: Install a composite pack from configured sources

- **WHEN** a user installs a composite pack whose referenced packs are present in configured sources
- **THEN** LunaPack resolves and installs the composite pack, all references, their used external-source requirements, and their managed files

#### Scenario: Resolve a composite reference from the earliest configured source

- **WHEN** equal ID-and-version composite candidates exist in multiple configured sources
- **THEN** LunaPack selects the candidate from the earliest configured source

#### Scenario: Resolve a Git-sourced composite reference

- **WHEN** a Git-sourced composite pack references an exact pack version available from configured Git or local sources
- **THEN** LunaPack resolves that reference using the same configured-source precedence as a direct installation

#### Scenario: Keep pack aliases scoped

- **WHEN** two packs in one graph use the same alias for different external-source fingerprints
- **THEN** LunaPack resolves each alias only within its declaring pack

#### Scenario: Refuse a missing composite reference

- **WHEN** a composite pack references an ID and version absent from configured sources
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

#### Scenario: Omit a conditional composite reference

- **WHEN** a composite reference condition evaluates to false
- **THEN** LunaPack omits that pack and dependencies reachable only through it, including their pack-defined external-source requirements

#### Scenario: Resolve an OCI dependency without catalog access

- **WHEN** a composite references a known OCI pack ID/version that its scoped
  credentials can read but cannot enumerate
- **THEN** Luna queries that repository directly and applies normal graph validation
