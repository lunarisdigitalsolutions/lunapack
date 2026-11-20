# Pack Resource Limits Specification

## Purpose

Bound resource use when Luna reads, resolves, renders, and materializes packs,
while allowing safe, explicit configuration for unusually large projects.

## Requirements

### Requirement: Apply finite resource-limit defaults

When no override exists, Luna SHALL apply finite defaults for every resource
limit, including existing OCI archive protections.

#### Scenario: Apply documented defaults

- **WHEN** a limit has no configured value in any Luna configuration scope
- **THEN** Luna applies these defaults: project and pack manifests, 1 MiB each;
  lock file, 64 MiB; pack documents, 16 MiB per document; pack snapshot, 2
  GiB; rendered content, 64 MiB per file and 512 MiB per lifecycle operation;
  graph, 64 edges deep, 1,024 unique nodes, and 100,000 traversed references;
  files, 10,000 per pack and 100,000 per resolved graph; project settings
  bootstrap, 16 MiB; user settings bootstrap, 1 MiB; OCI archives, 100,000
  entries, 512 MiB download, 512 MiB per entry, and 2 GiB expanded content

#### Scenario: Preserve current OCI archive defaults

- **WHEN** an OCI archive is processed without configured overrides
- **THEN** Luna enforces the existing entry-count, download, per-entry, and
  total-expansion defaults

### Requirement: Configure resource limits through Luna scopes

`luna config set`, `list`, and `rm` SHALL support typed positive-integer
overrides for resource limits in project, global-user, and user-project-specific
scopes. Project-controlled settings SHALL only lower the user-owned effective
value and built-in default; user-owned scopes MAY raise or lower defaults.
User-project settings SHALL take precedence over global-user settings, and no
value SHALL disable a limit. User-owned ceilings are finite, and project values
remain capped at built-in defaults.

#### Scenario: Apply finite user ceilings

- **WHEN** a user-owned global or user-project override is resolved
- **THEN** it is no greater than the matching value below

| Key                      |       Unit | Default | User ceiling |
| ------------------------ | ---------: | ------: | -----------: |
| `project-manifest-bytes` |      bytes |   1 MiB |       16 MiB |
| `pack-manifest-bytes`    |      bytes |   1 MiB |       64 MiB |
| `lock-file-bytes`        |      bytes |  64 MiB |        1 GiB |
| `pack-document-bytes`    |      bytes |  16 MiB |      256 MiB |
| `pack-snapshot-bytes`    |      bytes |   2 GiB |       16 GiB |
| `rendered-file-bytes`    |      bytes |  64 MiB |      256 MiB |
| `rendered-total-bytes`   |      bytes | 512 MiB |        4 GiB |
| `dependency-depth`       |      edges |      64 |          128 |
| `dependency-nodes`       |      nodes |   1,024 |      100,000 |
| `dependency-references`  | references | 100,000 |    1,000,000 |
| `files-per-pack`         |      files |  10,000 |      100,000 |
| `files-per-graph`        |      files | 100,000 |    1,000,000 |
| `oci-archive-entries`    |    entries | 100,000 |    1,000,000 |
| `oci-download-bytes`     |      bytes | 512 MiB |       16 GiB |
| `oci-entry-bytes`        |      bytes | 512 MiB |        4 GiB |
| `oci-expanded-bytes`     |      bytes |   2 GiB |       16 GiB |

#### Scenario: Set a project limit below its default

- **WHEN** a user configures a valid lower limit in project scope
- **THEN** Luna applies it for that project without changing global or
  user-project settings

#### Scenario: Reject a project limit above its default

- **WHEN** project-controlled configuration attempts to raise a built-in limit
- **THEN** Luna rejects the setting and reports its key and allowed range

#### Scenario: Raise a limit in a user-owned scope

- **WHEN** a user configures a finite limit above its default in global-user or
  user-project-specific scope
- **THEN** Luna applies it according to existing scope precedence

#### Scenario: Prevent a project from raising a stricter user limit

- **WHEN** a user-owned scope lowers a limit and project configuration declares
  a higher value
- **THEN** Luna keeps the stricter user-owned value

#### Scenario: Reject an invalid limit value

- **WHEN** a limit value is zero, negative, non-numeric, overflowing, or unknown
- **THEN** `luna config` returns a non-success result without changing
  configuration

#### Scenario: Reject a value above its finite hard ceiling

- **WHEN** a user-owned setting exceeds its documented hard ceiling
- **THEN** Luna rejects the setting without changing configuration

### Requirement: Bound configuration bootstrap reads

Luna SHALL apply compiled byte ceilings before parsing project or user settings
that contain resource-limit overrides. A settings document SHALL not be able to
raise the bootstrap ceiling that protects its own read.

#### Scenario: Reject oversized user settings before parsing

- **WHEN** `~/.lunapack/config.yml` exceeds its 1 MiB bootstrap ceiling
- **THEN** Luna rejects it before deserialization and does not fall back to
  unbounded parsing

#### Scenario: Keep project settings below the bootstrap ceiling

- **WHEN** project configuration attempts to raise the compiled project-settings
  bootstrap ceiling
- **THEN** Luna rejects the override before using it to read project inputs

### Requirement: Bound manifest, lock, and pack-document reads

Luna SHALL enforce the configured byte ceilings for `lunapack.yml`, `pack.yml`,
`lunapack-lock.yml`, and pack documentation before parsing or rendering their
contents. Byte counts SHALL be verified while reading, not only from metadata.

#### Scenario: Reject an oversized manifest before parsing

- **WHEN** the bytes read for a project or pack manifest exceed its effective
  limit
- **THEN** Luna rejects it before deserialization and reports the observed size
  and limit

#### Scenario: Reject an oversized lock file before parsing

- **WHEN** the bytes read for `lunapack-lock.yml` exceed its effective limit
- **THEN** Luna rejects it before deserialization and preserves existing project
  state

#### Scenario: Reject an oversized pack document before rendering

- **WHEN** Luna reads a README or lifecycle instruction document larger than
  its effective limit
- **THEN** Luna rejects it before rendering or displaying its content

### Requirement: Bound dependency graphs and selected files

Before lifecycle mutation, Luna SHALL reject a resolved graph exceeding its
effective maximum depth, unique-node count, or traversed-reference count, a pack
snapshot exceeding its byte or file count, or a resolved graph exceeding its
selected-file count. Budgets SHALL be shared across all requested roots.

#### Scenario: Reject an over-depth graph

- **WHEN** a resolved root-to-dependency path exceeds the configured maximum edge
  depth
- **THEN** Luna rejects the graph before changing project files or state

#### Scenario: Reject an over-sized dependency graph

- **WHEN** resolving another unique pack would exceed the configured graph-node
  limit
- **THEN** Luna rejects the graph before changing project files or state

#### Scenario: Reject excessive reference traversal

- **WHEN** traversing another composite reference would exceed the configured
  graph-reference limit
- **THEN** Luna stops traversal and rejects the graph before changing project
  files or state

#### Scenario: Reject excessive files

- **WHEN** a pack or resolved graph exceeds its configured file-count limit
- **THEN** Luna rejects it and reports the observed count and limit

#### Scenario: Reject an oversized pack snapshot

- **WHEN** pack files exceed the configured aggregate pack-snapshot byte limit
- **THEN** Luna rejects the pack during materialization or enumeration and
  reports the observed bytes and limit

### Requirement: Bound rendered content

Luna SHALL enforce both the configured per-file rendered-content limit and
cumulative rendered-content limit while producing output, before writing
managed files or lock state.

#### Scenario: Reject an oversized rendered file

- **WHEN** template output exceeds its effective per-file limit
- **THEN** Luna stops rendering, reports the observed size and limit, and
  preserves project state

#### Scenario: Reject excessive cumulative rendered content

- **WHEN** rendered files together exceed the effective lifecycle-operation limit
- **THEN** Luna rejects the operation before applying managed-file or lock changes

### Requirement: Preserve and configure OCI archive protections

OCI archive entry-count, download, per-entry, and expanded-content checks SHALL
use the same effective Luna resource-limit configuration. Overrides SHALL not
bypass archive path, digest, duplicate-entry, or link protections.

#### Scenario: Reject an archive that exceeds an effective limit

- **WHEN** an OCI archive exceeds an effective entry-count or byte limit
- **THEN** Luna rejects it before extraction and reports the observed value,
  limit, and unit

#### Scenario: Keep unsafe archive entries rejected after an override

- **WHEN** an archive is within configured size limits but contains an unsafe
  path or link-like entry
- **THEN** Luna rejects it without writing outside its extraction root

### Requirement: Report limit failures without project mutation

Every resource-limit rejection SHALL identify the input or operation, observed
value (or observed lower bound for a stopped stream), configured limit, and
unit. Rejection SHALL occur before managed-file, project-configuration, or
lock-file mutation.

#### Scenario: Preserve state after a limit rejection

- **WHEN** any pack-processing limit is exceeded during install, update,
  validation, or extraction
- **THEN** Luna returns a non-success result and leaves project files and
  persisted state unchanged

#### Scenario: Report a streaming lower bound

- **WHEN** Luna stops reading or rendering as soon as a stream exceeds its limit
- **THEN** the diagnostic reports the minimum observed value and the configured
  limit without exposing input content
