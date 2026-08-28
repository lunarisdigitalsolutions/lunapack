# CLI Project Configuration Delta Specification

## MODIFIED Requirements

### Requirement: Register a Git pack source

The `luna sources add git <name> <repository-url>` command SHALL add a named Git source to the current directory's `lunapack.yml`. It SHALL accept optional `--ref <branch-tag-or-commit>` and `--path <repository-relative-path>` arguments, canonicalize a supplied symbolic ref and safe base path before persistence, and reject duplicate names, embedded credentials, invalid or ambiguous refs, unsafe paths, or a fingerprint already configured under any name. A missing ref SHALL retain existing workspace remote-HEAD behavior. The command SHALL preserve existing configuration when arguments, source resolution, or project state are invalid.

#### Scenario: Add a Git source with a branch and path

- **WHEN** a user runs `luna sources add git shared-packs <repository-url> --ref main --path packs` after initialization and `main` resolves as a branch
- **THEN** `lunapack.yml` contains one Git source named `shared-packs` with the repository URL, `refs/heads/main`, and normalized `packs` path

#### Scenario: Add a Git source with defaults

- **WHEN** a user runs `luna sources add git shared-packs <repository-url>` after initialization
- **THEN** `lunapack.yml` contains one Git source named `shared-packs` with that repository URL and no explicit ref or path

#### Scenario: Reject a duplicate Git source

- **WHEN** a user adds a Git source whose normalized fingerprint equals an existing configured source under another name
- **THEN** LunaPack identifies the authoritative existing source and leaves project configuration unchanged

### Requirement: Register named remote pack sources

The `luna sources add git <name> <repository-url>` and `luna sources add github <name> <owner/repository>` commands SHALL require and persist a unique non-empty source name while retaining their ref, path, URL conversion, validation, and fingerprint-uniqueness behavior. GitHub shorthand SHALL accept exactly one owner and repository segment for `github.com`, reject hostnames and complete URLs, require `--ref`, and persist a normal Git source URL with a canonical ref. GitHub Enterprise repositories SHALL use the `git` variant.

#### Scenario: Add a named Git source

- **WHEN** a user runs `luna sources add git shared-packs <repository-url> --ref main --path packs`
- **THEN** `lunapack.yml` contains a Git source named `shared-packs` with the supplied URL, canonical ref, and normalized path

#### Scenario: Add a named GitHub source

- **WHEN** a user runs `luna sources add github public-packs lunarisdigitalsolutions/packs --ref main`
- **THEN** `lunapack.yml` contains a Git source named `public-packs` with URL `https://github.com/lunarisdigitalsolutions/packs.git` and ref `refs/heads/main`

#### Scenario: Reject invalid GitHub shorthand

- **WHEN** a user supplies a hostname, complete URL, missing ref, or value other than `owner/repository` to `luna sources add github`
- **THEN** LunaPack returns a non-success result without changing project configuration

#### Scenario: Reject a reused source name

- **WHEN** a user adds a remote source using the name of any configured source
- **THEN** LunaPack returns a non-success result without changing project configuration

### Requirement: Remove a configured source safely

`luna sources rm <name>` SHALL remove the source with that exact case-sensitive name only when `lunapack-lock.yml` records no installed pack or link consumer for it. In the same atomic project-state update, Luna SHALL remove project-scoped source and pack trust entries bound to that source name. It SHALL retain trust entries and configuration for all other sources and SHALL not automatically remap consumers.

#### Scenario: Remove a source while others remain

- **WHEN** a user removes an unconsumed configured source and at least one configured source remains
- **THEN** Luna persists the source and associated trust removal, confirms success, and recommends `luna sources list` and `luna discover`

#### Scenario: Remove the last source

- **WHEN** a user removes the only configured source and it has no lock-file consumers
- **THEN** Luna persists the source and associated trust removal, reports that no sources remain, and recommends `luna sources add git <name> <repository-url>`

#### Scenario: Remove a source used by an installed pack

- **WHEN** an installed pack or link lock record names the requested source
- **THEN** Luna identifies each consumer and returns a non-success result without changing configuration, trust, lock state, or managed files

#### Scenario: Reject an unknown source name

- **WHEN** a user runs `luna sources rm <name>` for a name that is not configured
- **THEN** Luna returns a non-success result without changing configuration, trust, lock state, or managed files

## ADDED Requirements

### Requirement: Validate workspace source fingerprint uniqueness

Every operation that loads, validates, or writes `lunapack.yml`, including source addition, link addition, pack installation or update, and source rename, SHALL reject duplicate normalized source fingerprints. Fingerprint uniqueness SHALL be limited to the effective source collection in one `lunapack.yml`.

#### Scenario: Reject manually duplicated sources

- **WHEN** a workspace configuration contains differently named sources with the same normalized fingerprint
- **THEN** LunaPack identifies both names and returns a non-success result before changing configuration, files, or lock state

### Requirement: Rename a configured source atomically

`luna sources rename <current-id> <new-id>` SHALL require a valid unused identifier and SHALL atomically update the source key, lock-file source references, link references, and workspace-level references while preserving the source fingerprint. Pack manifests SHALL remain unchanged because they use pack-local aliases. When a manually renamed source has one unambiguous fingerprint match to prior lock state, LunaPack SHALL request explicit approval before repairing references; ambiguous changes SHALL require the rename command.

#### Scenario: Rename a consumed source

- **WHEN** a user renames a configured source to a valid unused identifier
- **THEN** LunaPack updates every authoritative workspace reference and preserves every pack-local alias in one project-state transaction

#### Scenario: Reject a colliding rename

- **WHEN** the requested new identifier already exists
- **THEN** LunaPack returns a non-success result without changing configuration or lock state
