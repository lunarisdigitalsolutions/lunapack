# Git Pack Sources Delta Specification

## ADDED Requirements

### Requirement: Normalize source identity and enforce fingerprint uniqueness

LunaPack SHALL derive a source fingerprint independently of its configured identifier. A Git fingerprint SHALL combine a sanitized normalized repository identity, canonical Git ref, and normalized repository-relative base path. Supported HTTPS, SSH, and scp-style URLs for the same repository SHALL share an identity; transport and embedded credentials SHALL not be part of it. For `github.com`, host, owner, and repository names SHALL be lowercase and an optional `.git` suffix and trailing separators SHALL be removed. The base path SHALL use `/`, collapse redundant separators and `.` segments, reject escaping `..` segments, omit unnecessary trailing separators, and represent the repository root as `/`. A local workspace source fingerprint SHALL use its canonical filesystem path and SHALL remain distinct from every Git fingerprint. `lunapack.yml` SHALL contain at most one source for each fingerprint.

#### Scenario: Match GitHub HTTPS and SSH forms

- **WHEN** two Git declarations identify the same GitHub repository, canonical ref, and base path through supported HTTPS and SSH URL forms
- **THEN** LunaPack computes the same fingerprint for both declarations

#### Scenario: Distinguish source roots

- **WHEN** two Git declarations use the same repository and canonical ref but different normalized base paths
- **THEN** LunaPack computes different fingerprints

#### Scenario: Reject duplicate configured fingerprints

- **WHEN** two workspace source identifiers normalize to the same fingerprint
- **THEN** LunaPack rejects the workspace configuration before catalog or lifecycle mutation

#### Scenario: Keep local and Git sources distinct

- **WHEN** a local source points at a checkout of a configured Git repository
- **THEN** LunaPack computes different local and Git fingerprints

### Requirement: Canonicalize requested Git refs

LunaPack SHALL preserve an exact commit as an immutable ref identity and SHALL canonicalize a resolvable symbolic ref to its complete Git ref before fingerprint matching or persistence for a pack-defined source. A short branch name SHALL canonicalize to `refs/heads/<name>` and a short tag name SHALL canonicalize to `refs/tags/<name>`. LunaPack SHALL reject a short ref that resolves to both a branch and a tag and SHALL require the complete ref. Workspace Git sources MAY omit a ref and SHALL retain existing remote-HEAD behavior; a source without an unambiguous effective ref SHALL not satisfy a pack-defined source requirement.

#### Scenario: Canonicalize a branch

- **WHEN** `main` resolves only as a remote branch
- **THEN** LunaPack uses `refs/heads/main` in the source fingerprint and persisted pack-defined source

#### Scenario: Reject an ambiguous short ref

- **WHEN** a short ref resolves to both a branch and a tag
- **THEN** LunaPack returns a non-success result that identifies both complete ref choices

#### Scenario: Distinguish commit and branch identities

- **WHEN** a branch and an exact commit currently resolve to the same commit
- **THEN** LunaPack computes different fingerprints because their update semantics differ

### Requirement: Materialize external source content safely

LunaPack SHALL resolve each approved external Git source to an immutable commit before reading selected content. It SHALL confine source paths and followed symbolic links to the resolved source root, SHALL never execute external-source content, and SHALL use the existing Git timeout, cancellation, credential, and process-isolation rules. Equivalent source fingerprints at the same resolved commit SHALL share reconstructable cache content keyed by fingerprint and commit, regardless of URL transport or pack alias.

#### Scenario: Reuse one cached external source

- **WHEN** multiple packs require equivalent source fingerprints that resolve to the same commit
- **THEN** LunaPack may materialize one cache entry and use it for every mapped pack alias

#### Scenario: Reject source-root escape

- **WHEN** an external selector or followed symbolic link resolves outside the approved source root
- **THEN** LunaPack returns a non-success result before changing project configuration, managed files, or lock state

#### Scenario: Keep credentials out of persisted identity

- **WHEN** source access uses credentials supplied by supported Git credential handling
- **THEN** LunaPack omits those credentials from fingerprints, configuration, lock state, console output, audit output, and logs
