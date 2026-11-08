# Spec Delta

## Purpose

Authenticate private OCI source access transparently while protecting secrets,
isolating source credentials, and keeping interactive and CI behavior predictable.

## ADDED Requirements

### Requirement: Authenticate only when source access needs it

Adding an OCI source SHALL require neither network access nor credentials.
When an operation requires authentication, Luna SHALL resolve available
credentials, authenticate, and retry the original operation automatically.
There SHALL be no `luna sources login` or `luna sources logout` commands.

#### Scenario: Register an inaccessible private registry

- **WHEN** a user adds a syntactically valid private OCI source
- **THEN** Luna saves nonsecret source configuration without contacting the
  registry, obtaining tokens, opening the credential store, or prompting

#### Scenario: Read a public artifact

- **WHEN** anonymous OCI access succeeds
- **THEN** Luna completes the operation without requesting credentials

### Requirement: Prompt securely and resume the command

Interactive authentication SHALL identify source name, registry, and path,
request a username when unavailable, and collect a non-echoed password/token.
After validation Luna SHALL report authentication success and resume the
original command without requiring a rerun. Cancellation SHALL stop that
source's access without recording unvalidated credentials.

#### Scenario: Install a private pack on first access

- **WHEN** `luna install backend` needs private source `company` and no usable
  credentials exist in an interactive session
- **THEN** Luna prompts securely, validates the entered credentials, and continues
  installation within the same invocation

### Requirement: Persist only successfully validated credentials securely

Validated interactive OCI credentials SHALL be saved automatically using
Windows Credential Manager, macOS Keychain, or Linux Secret Service/compatible
keyring. Luna SHALL reuse them for later source requests. It SHALL NOT store
secrets in project files or use reversible encryption with a colocated key.

#### Scenario: Reuse credentials after restarting Luna

- **WHEN** a later command accesses the same source after successful authentication
- **THEN** Luna can retrieve its credential from the native user store without
  another password prompt

#### Scenario: Report an unavailable keyring

- **WHEN** the OS store is unavailable or cannot save a validated credential
- **THEN** Luna explicitly reports that secure persistence failed, never writes a
  plaintext fallback, and distinguishes operation success from persistence failure

### Requirement: Isolate credentials by configured source identity

Credential matching SHALL include canonical project identity, configured source
name, OCI registry endpoint, and source path. Credentials SHALL not transfer
between distinct sources merely because they share a host. Endpoint/path
changes and source renames SHALL not silently reuse an old identity's secrets.

#### Scenario: Use different credentials on one registry

- **WHEN** `customer-a` and `customer-b` use the same host with different paths
- **THEN** each retrieves and saves only credentials bound to its own source

#### Scenario: Reuse a source name in another project

- **WHEN** another project configures `company` with a different source identity
- **THEN** that source does not inherit the first project's credential

### Requirement: Replace rejected stored credentials without prompt loops

Rejected stored credentials SHALL be excluded from further retries in that
invocation. Interactive Luna SHALL offer one replacement prompt per source,
validate the replacement, and replace stored credentials only on success.
Invalid replacement credentials SHALL produce a source-scoped failure, not an
unbounded retry or prompt loop.

#### Scenario: Recover from an expired stored token

- **WHEN** a stored credential fails and a replacement authenticates successfully
- **THEN** Luna replaces the stored credential and resumes the original command

#### Scenario: Reject another invalid credential

- **WHEN** the replacement credential is also rejected
- **THEN** Luna reports authentication failure without prompting repeatedly or
  saving the rejected replacement

### Requirement: Never wait for authentication input in CI

Noninteractive invocations SHALL never prompt. Missing, expired, or rejected
credentials SHALL report source name, registry/path, and unavailable usable
credentials. Required-source access SHALL return nonzero; optional-source
failure SHALL preserve results from other sources.

#### Scenario: Fail required CI authentication

- **WHEN** noninteractive publication needs `company` and no provider authenticates
- **THEN** Luna exits nonzero immediately without reading user input

#### Scenario: Search other sources in CI

- **WHEN** an optional private source cannot authenticate during catalog browsing
- **THEN** Luna warns for that source and continues browsing accessible sources

### Requirement: Accept source-bound noninteractive registry credentials

Luna SHALL accept generic username/password or username/token credentials
from a documented noninteractive provider bound to one configured source.
Such credentials SHALL work for ACR, GHCR, Harbor, and compatible registries
without being serialized or automatically persisted from CI.

#### Scenario: Publish to GHCR using a CI token

- **WHEN** CI supplies valid source-bound username/token credentials
- **THEN** `luna publish ./packs/backend --target private` authenticates and
  publishes without a login command or credential-file mutation

### Requirement: Support optional Azure identity for ACR

ACR sources SHALL support generic credentials and optional Azure identity
authentication, including developer identities and federated CI identities.
Azure-specific credential availability SHALL not be a requirement for generic
OCI use. Unavailable Azure identity SHALL yield actionable diagnostics and
allow other supported authentication methods.

#### Scenario: Use federated Azure identity

- **WHEN** an ACR source is accessed with a usable workload-federated Azure identity
- **THEN** Luna obtains registry access and continues without a permanent client
  secret or separate Luna login

#### Scenario: Use an ACR username and password

- **WHEN** a valid source-bound ACR username/password is available without Azure CLI
- **THEN** Luna can read or publish through normal OCI authentication

### Requirement: Keep secrets out of all observable and persisted state

Passwords, tokens, access/refresh tokens, authorization headers, and embedded
URL credentials SHALL never appear in configuration, lock files, user settings,
fingerprints, logs, diagnostics, telemetry, exception messages, or normal output.
An optional username SHALL be the only credential information allowed in shared
source configuration.

#### Scenario: Diagnose a rejected token

- **WHEN** authentication fails with a token present in a provider or registry error
- **THEN** output identifies the source and failure without exposing the token,
  credential-bearing URL, or authorization header

### Requirement: Separate authentication from authorization

Luna SHALL distinguish a missing/invalid authentication context from a valid
identity lacking requested repository permissions. It SHALL retry only bounded
authentication failures and SHALL not continually prompt on authenticated
`403 Forbidden`, registry outage, DNS, or TLS failures.

#### Scenario: Reject read-only credentials during publishing

- **WHEN** a validated identity can read but cannot push the target repository
- **THEN** Luna reports insufficient publishing permission for that source rather
  than falsely reporting successful publication or looping through passwords
