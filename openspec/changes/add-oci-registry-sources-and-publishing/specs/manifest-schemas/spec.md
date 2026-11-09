# Spec Delta

## ADDED Requirements

### Requirement: Define nonsecret OCI project sources

The project schema SHALL add `oci` sources with required registry `url`,
optional repository `path`, and optional nonsecret `username`. Names SHALL use
the existing sources-map contract. Secrets, resolved digests, ignore state,
OCI Git refs, and unknown properties SHALL be rejected. Existing local/Git
configurations SHALL remain valid without migration.

#### Scenario: Validate two source paths on one registry

- **WHEN** `lunapack.yml` configures `company` and `customer` as OCI sources with
  the same URL and different paths
- **THEN** schema/runtime validation accepts the independent source identities

#### Scenario: Reject credential-bearing configuration

- **WHEN** an OCI declaration contains `password`, `token`, `accessToken`,
  `refreshToken`, or credentials embedded in its URL
- **THEN** Luna rejects it without echoing the secret in validation output

### Requirement: Extend immutable source identity with OCI

Configured-source identity schemas SHALL include OCI endpoint/path identity
for trust, lock matching, and user-local state. Immutable source identity SHALL
exclude username, credentials, resolved version, and digest. Runtime validation,
serialization, public schemas, and distributed schema references SHALL agree.

#### Scenario: Keep release resolution separate from source identity

- **WHEN** a source publishes a new version or changes its optional username
- **THEN** its configured identity remains the same while lock resolution records
  retain exact release evidence

### Requirement: Define exact OCI lock provenance

The lock schema SHALL support OCI pack provenance with source name/type,
normalized endpoint/path, repository, exact manifest version, resolved manifest
digest, and content digest. Secrets and ignore state SHALL be invalid lock
properties. Existing local/Git lock records SHALL remain readable unchanged.

#### Scenario: Validate an OCI dependency lock

- **WHEN** installation resolves an OCI root and transitive OCI pack
- **THEN** each record has exact source and digest provenance without credential data

#### Scenario: Preserve a legacy lock

- **WHEN** a valid local/Git lock has no OCI fields
- **THEN** Luna accepts it without synthesizing unrelated OCI provenance

### Requirement: Extend existing project-local user settings

The user-settings schema SHALL add ignored configured-source identities under
each canonical project's existing local settings and add OCI identity support
to trust/acknowledgement entries. Existing settings without ignored sources
SHALL mean none are ignored. Shared project and lock schemas SHALL not admit
this user preference.

#### Scenario: Preserve user trust when ignoring

- **WHEN** Luna saves an ignored source for one project
- **THEN** existing project/global trust, acknowledgements, and denial settings
  remain valid and unchanged

#### Scenario: Reject portable ignore state

- **WHEN** `lunapack.yml` or `lunapack-lock.yml` contains ignored-source settings
- **THEN** schema validation rejects them as user-local state
