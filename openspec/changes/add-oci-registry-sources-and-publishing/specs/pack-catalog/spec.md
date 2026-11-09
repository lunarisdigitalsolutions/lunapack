# Spec Delta

## ADDED Requirements

### Requirement: Include OCI releases in the normal catalog

OCI releases SHALL participate in `luna discover`, `luna search <term>`,
available-version listing, and `luna inspect <id>[@<version>]` with existing
semantic-version selection, metadata ranking, output shape, and configured
source precedence. OCI inspection SHALL expose normal pack details from
validated pack content rather than a separate registry-specific UI.

#### Scenario: Select among mixed sources

- **WHEN** local, Git, and OCI sources offer the same pack ID/version
- **THEN** Luna uses the existing earliest-configured-source tie-breaker and
  identifies the selected source as normal

#### Scenario: List OCI versions

- **WHEN** an OCI repository has versions `1.0.0`, `1.4.0`, and `2.0.0`
- **THEN** normal discovery/search version limits use descending semantic versions

### Requirement: Preserve accessible results after source failures

Catalog browsing SHALL track each source's authentication, permission,
availability, timeout, DNS, and TLS failures independently. If other sources
were searched successfully, Luna SHALL retain their results and emit
source-specific warnings and a partial-search summary. It SHALL not change
remaining sources' precedence positions.

#### Scenario: Search public and customer sources after a company failure

- **WHEN** `public` and `customer` are accessible but `company` cannot authenticate
- **THEN** Luna returns their results, warns for `company`, and reports that other
  configured sources were searched successfully

#### Scenario: Report complete access failure

- **WHEN** every nonignored configured source fails during browsing
- **THEN** Luna returns nonzero with source-specific failures rather than reporting
  a successful empty catalog

#### Scenario: Distinguish intentionally ignored sources

- **WHEN** every configured source is ignored locally
- **THEN** Luna reports that no active sources were searched, without contacting
  them or pretending an access failure occurred

### Requirement: Fail when an inaccessible source is required

An explicitly selected, locked, or required dependency source SHALL not be
silently replaced after failure. Luna SHALL identify the required source and
failure cause, return nonzero, and preserve project state. Unresolved queries
with failed candidate sources SHALL report incomplete resolution rather than
asserting the pack definitively does not exist.

#### Scenario: Inspect a release locked to an inaccessible source

- **WHEN** inspection requires source `company` and it cannot authenticate
- **THEN** Luna reports that source's authentication failure rather than inspecting
  another source's release silently

#### Scenario: Find no result in a partial search

- **WHEN** accessible sources lack the requested pack and another source failed
- **THEN** Luna reports that it could not resolve the pack from accessible sources
  and identifies the unsearched source
