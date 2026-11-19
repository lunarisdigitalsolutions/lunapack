# CLI Diagnostics Specification

## Purpose

Defines a versioned JSON result and diagnostic contract for selected Luna CLI
commands while preserving their existing text behavior.

## Requirements

### Requirement: Select a diagnostic output format

The root `luna audit`, `luna outdated`, and `luna validate <pack-reference>`
commands SHALL accept `--format text|json`. Omitting the option SHALL select
text output. The separate local-manifest `luna pack validate` command is not
part of this contract.

#### Scenario: Preserve text output by default

- **WHEN** a user runs a supported command without `--format`
- **THEN** Luna emits the existing text presentation and retains the command's
  existing exit behavior

#### Scenario: Select JSON output

- **WHEN** a user runs a supported command with `--format json`
- **THEN** Luna emits that command's versioned JSON result

#### Scenario: Reject an unsupported format

- **WHEN** a user supplies a `--format` value other than `text` or `json`
- **THEN** Luna returns a non-success result, writes the format error to
  standard error, and writes no command output to standard output

### Requirement: Emit a versioned command result

In JSON mode, each supported command SHALL emit one JSON object with integer
`schemaVersion` 1, `command` set to `audit`, `outdated`, or `validate`, and a
command-specific `result`. Audit JSON objects SHALL also contain top-level
`findings` and `summary` properties.

#### Scenario: Emit one versioned envelope

- **WHEN** a supported command completes in JSON mode
- **THEN** its standard output contains one JSON object with
  `schemaVersion: 1`, the matching `command` value, and its command-specific
  `result`

### Requirement: Preserve audit data as structured fields

The audit result SHALL expose packs, links, external sources, and external files
as arrays of stable camel-case fields. Array properties SHALL remain present
when empty.

#### Scenario: Serialize an audit result

- **WHEN** `luna audit --format json` completes its inspection
- **THEN** its result contains packs, links, external sources, and external
  files as structured data, and the JSON object contains findings and summary

  - Pack entries contain `id`, `version`, `sourcePath`, `packPath`,
    `dependencies` (`id`, `version`), and `managedFiles` (`targetPath`,
    `sha256`).
  - Link entries contain `name`, `sourceName`, `resolvedCommit`, and `files`
    (`targetPath`, `status`).
  - External-source entries contain `packId`, `packVersion`, `alias`,
    `workspaceSourceName`, `fingerprint`, `ref`, `resolvedCommit`, and
    `status`.
  - External-file entries contain external-source fields plus `sourcePath`,
    `targetPath`, and `sha256`.
  - Status values are `current`, `missing-workspace-source`,
    `invalid-workspace-source`, `configuration-drift`, `missing-target`,
    `modified-target`, or `conflicting-owner`.
  - Unavailable optional `sourcePath` or `resolvedCommit` values are `null`.

### Requirement: Preserve outdated data as structured fields

The outdated result SHALL expose update, link, OCI-integrity, and unchecked-OCI
data as arrays, plus a Boolean `remoteRefsChecked`. Array properties SHALL
remain present when empty.

#### Scenario: Serialize outdated results

- **WHEN** `luna outdated --format json` completes its check
- **THEN** its result contains available pack updates, outdated links, OCI
  integrity violations, unchecked OCI packs, and whether remote refs were
  checked as structured data
  - Update entries contain `id`, `currentVersion`, `latestVersion`, and
    `reason`.
  - Outdated-link entries contain `name` and `reasons`.
  - Integrity-violation entries contain `id`, `version`, `sourceName`,
    `lockedManifestDigest`, and `observedManifestDigest`.
  - Unchecked-OCI entries contain `id`, `version`, and `sourceName`.
  - `remoteRefsChecked` is false when `--offline` is supplied.
  - Unavailable `sourceName` or `observedManifestDigest` values are `null`.

#### Scenario: Refuse incomplete remote evidence

- **WHEN** an outdated check cannot completely inspect every configured remote
  source or the selected source returns partial release discovery
- **THEN** Luna treats the command as unable to produce a result, writes
  `result: null` to standard output, writes the incomplete-source diagnostic
  to standard error, and returns a non-success status
  - Luna SHALL NOT emit `remoteRefsChecked: true` from partial evidence.

#### Scenario: Serialize pack validation status

- **WHEN** `luna validate <pack-reference> --format json` completes validation
- **THEN** its result contains a Boolean `valid` value

#### Scenario: Represent invalid pack validation

- **WHEN** a selected pack fails validation
- **THEN** the JSON result contains `valid: false`, validation issues are
  written to standard error, and the existing non-success exit behavior remains

#### Scenario: Preserve parseable output when a command fails

- **WHEN** a supported command starts in JSON mode but cannot produce its
  result
- **THEN** Luna writes a valid JSON object with `result` set to `null` and
  reports the human-readable failure on standard error
  - An audit error also returns an empty `findings` array and zero-valued
    counts for every severity.

### Requirement: Report audit findings with stable codes

Each audit finding SHALL include a stable `code`, `severity` (`info`, `warning`,
or `error`), a `subject` object, and a human-readable `message`. Subject kinds
are `external-source`, `external-file`, and `link-file`. Codes and subject
identity SHALL remain stable within schema version 1; consumers SHALL not rely
on message wording.

#### Scenario: Report an external-source finding

- **WHEN** an audited external source is missing, invalid, or differs from its
  locked configuration
- **THEN** Luna emits a coded finding identifying the source alias and owning
  pack in its subject

#### Scenario: Report an external-file finding

- **WHEN** an audited external file target is missing or locally modified
- **THEN** Luna emits a coded finding identifying the project-relative target
  path and owning pack in its subject

#### Scenario: Report a link-file finding

- **WHEN** an audited link target is missing, modified, or has conflicting
  ownership
- **THEN** Luna emits a coded finding identifying the link and target

#### Scenario: Assign stable codes and severities

- **WHEN** audit finds one of the following non-current states
- **THEN** it uses the matching code and severity:

  | State | Code | Severity |
  | --- | --- | --- |
  | Missing workspace source | `audit.external-source.missing-workspace` | `warning` |
  | Invalid workspace source | `audit.external-source.invalid-workspace` | `error` |
  | Source configuration drift | `audit.external-source.configuration-drift` | `warning` |
  | Missing external-file target | `audit.external-file.missing-target` | `warning` |
  | Modified external-file target | `audit.external-file.modified-target` | `warning` |
  | Missing link-file target | `audit.link-file.missing-target` | `warning` |
  | Modified link-file target | `audit.link-file.modified-target` | `warning` |
  | Conflicting link-file owner | `audit.link-file.conflicting-owner` | `error` |

#### Scenario: Summarize findings

- **WHEN** audit produces findings with different severities
- **THEN** `summary.totalFindings` and every `summary.bySeverity` count match
  the findings array

### Requirement: Identify audit subjects and summarize findings

External-source and external-file subjects SHALL contain alias `id` and owning
`packId`; link-file subjects SHALL contain link-name `id`. File subjects SHALL
include a project-relative `path`. The summary SHALL contain integer
`totalFindings` and integer `bySeverity` counts for all three severities,
including zeroes.

#### Scenario: Expose stable subject fields and severity counts

- **WHEN** audit emits external-source, external-file, or link-file findings
- **THEN** each subject includes its required identifiers and project-relative
  file path, and the summary includes zero or positive integer counts for
  `info`, `warning`, and `error`

### Requirement: Keep JSON output separate from human-readable diagnostics

In JSON mode, standard output SHALL contain only the command's JSON object,
without tables, banners, progress messages, or ANSI sequences. Human-readable
diagnostics and logs SHALL be written to standard error. Structured audit
findings SHALL remain in the JSON object.

#### Scenario: Separate audit findings from logs

- **WHEN** audit emits JSON findings and a human-readable diagnostic
- **THEN** standard output remains one parseable JSON object and the diagnostic
  appears on standard error

#### Scenario: Keep failure status independent of severity

- **WHEN** JSON output contains findings
- **THEN** the command retains its existing exit-code semantics

#### Scenario: Route JSON-mode help away from machine output

- **WHEN** a supported command is invoked with `--format json --help`
- **THEN** generated help is written to standard error, standard output is
  empty, and help retains the normal successful exit status

#### Scenario: Reject an invalid format before help

- **WHEN** a supported command is invoked with an unsupported `--format` and
  `--help`
- **THEN** Luna writes only the format error to standard error, leaves standard
  output empty, and returns a non-success status

### Requirement: Preserve compatibility of the text and JSON contracts

The text format SHALL remain the default and preserve existing command behavior.
Within JSON schema version 1, changes SHALL be additive; removing, renaming, or
changing the type or meaning of an existing field or finding code SHALL require
a new schema version.

#### Scenario: Preserve the current default for scripts

- **WHEN** an existing script invokes a supported command without `--format`
- **THEN** the command continues to use its existing text output

#### Scenario: Extend a compatible JSON schema

- **WHEN** a later compatible release adds a field or finding code
- **THEN** it retains schema version 1 and preserves existing fields and codes
