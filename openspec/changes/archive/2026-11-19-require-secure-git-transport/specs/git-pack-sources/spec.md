# Spec Delta

## ADDED Requirements

### Requirement: Deny insecure Git transports by default

LunaPack SHALL deny Git network operations using `http://` or `git://` when
`git.allow-insecure-transport` resolves to `false`. It SHALL deny the operation
before network access and without changing project configuration, lock state,
or catalog cache. This policy SHALL apply to source addition, ref resolution,
catalog discovery, pack materialization, Git-backed links, and external Git
sources.

#### Scenario: Block an insecure source without an override

- **WHEN** a Git operation targets an `http://` or `git://` source and the
  effective preference is `false`
- **THEN** LunaPack returns a non-success result before network access and
  identifies `git.allow-insecure-transport` as the opt-in

#### Scenario: Preserve secure and local Git sources

- **WHEN** a Git operation targets an HTTPS, SSH, scp-style, or local source
- **THEN** the insecure-transport preference does not block the operation

### Requirement: Prevent secure Git transport bypasses

LunaPack SHALL block Git URL rewrites from a secure source to `http://` or
`git://`. Secure HTTPS Git operations SHALL NOT follow redirects, so an HTTPS
source cannot downgrade to plaintext through a redirect. Secure HTTPS
operations SHALL require certificate verification. This also means an
HTTPS-to-HTTPS redirect fails.

#### Scenario: Block an insecure URL rewrite

- **WHEN** Git configuration rewrites an HTTPS source to `http://` or `git://`
- **THEN** Git blocks the rewritten transport before connecting to it

#### Scenario: Do not follow an HTTPS redirect

- **WHEN** an HTTPS Git server responds with a redirect
- **THEN** LunaPack fails the operation without following the redirect

#### Scenario: Reject disabled HTTPS verification

- **WHEN** inherited Git configuration disables HTTPS certificate verification
- **THEN** LunaPack overrides the setting or fails closed before network access

#### Scenario: Do not interpret a source as a Git option

- **WHEN** a repository location begins with `-` or uses unsafe scp-style
  syntax
- **THEN** LunaPack rejects it before invoking Git

#### Scenario: Reject an external smudge transport

- **WHEN** repository attributes select a smudge filter, including Git LFS
- **THEN** LunaPack fails before checkout and no smudge process or external
  endpoint is invoked

#### Scenario: Reject a hosted local-filesystem transport

- **WHEN** a configured or rewritten network source uses UNC or a hosted
  `file://` location
- **THEN** LunaPack fails before filesystem or network access

#### Scenario: Reject ambiguous URL-specific configuration

- **WHEN** a Git repository URL contains a query or fragment
- **THEN** LunaPack rejects the URL before network access

#### Scenario: Preserve exact effective URL semantics

- **WHEN** Git resolves or verifies a secure source URL
- **THEN** LunaPack preserves the exact URL bytes, rejects surrounding
  whitespace or undecodable output, and uses the same repository context as the
  requested operation

#### Scenario: Bind HTTPS settings without normalization drift

- **WHEN** an HTTPS repository location contains a raw backslash or any value
  Git matches differently from a normalized .NET URI
- **THEN** LunaPack rejects it, or binds and verifies security settings using the
  exact effective URL text

### Requirement: Warn when insecure Git transport is enabled

When `git.allow-insecure-transport` resolves to `true`, LunaPack SHALL warn once
per insecure source per CLI operation before network access. The warning SHALL
identify the source without exposing URL credentials.

#### Scenario: Use an opted-in insecure source

- **WHEN** a Git operation targets an `http://` or `git://` source and the
  effective preference is `true`
- **THEN** LunaPack warns before network access and may proceed

### Requirement: Keep transport policy separate from Git source identity

LunaPack SHALL continue deriving Git source identity independently of transport
policy. Denying or allowing an insecure URL SHALL NOT rewrite its configured
URL or change its normalized source fingerprint or lock ownership identity.

#### Scenario: Deny transport without changing source identity

- **WHEN** an insecure Git source is denied by the effective preference
- **THEN** LunaPack leaves the configured URL and persisted source identity
  unchanged

## MODIFIED Requirements

### Requirement: Diagnose Git authentication through existing credentials

Git authentication for secure transports SHALL continue using Git Credential
Manager, credential helpers, SSH configuration/agent, and supported environment
configuration. For plaintext HTTP operations enabled by the preference,
LunaPack SHALL NOT invoke Git credential helpers. Luna SHALL NOT collect or
store Git passwords. Authentication failures SHALL identify the source,
recommend its Git credential manager or SSH configuration, and explain local
ignoring as an option.

#### Scenario: Fail a private Git clone

- **WHEN** source `engineering` fails Git access because authentication was
  rejected
- **THEN** Luna reports `Unable to authenticate to Git source 'engineering'`,
  recommends Git credential-manager/SSH authentication, and mentions
  `luna sources ignore engineering` without prompting for a Git password

#### Scenario: Skip credential helpers for opted-in plaintext HTTP

- **WHEN** an HTTP Git operation is enabled by the preference and Git
  credential helpers are configured
- **THEN** LunaPack does not invoke those helpers for the operation
