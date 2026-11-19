# Design

## Context

See [proposal.md](proposal.md) for motivation and
[cli-diagnostics](specs/cli-diagnostics/spec.md) for the contract. `CliApplication`
builds the root command tree and supplies separate invocation output and error
writers, but `CliConsole` currently routes all presentation through one
`IAnsiConsole`. Audit combines pack and external-source data from `AuditService`
with link data from `LinkLifecycleService`; outdated combines pack-update,
OCI-integrity, and link reports. Both handlers currently render output as they
collect it, while validate writes result issues through `CliConsole`.

## Goals / Non-Goals

**Goals:**

- Keep the JSON contract independent of internal lock-file and service result
  types.
- Emit one complete JSON object to stdout only after command data is ready.
- Preserve existing text output, defaults, and exit-code semantics.
- Make audit findings and counts deterministic and centrally mapped.

**Non-Goals:**

- Change command selection, validation rules, or lifecycle behavior.
- Add JSON formats to other commands or code every non-audit diagnostic in this
  increment.
- Change failure status based on finding severity.

## Decisions

### Use command-scoped format selection and explicit v1 DTOs

Add a shared `text|json` option helper to the three supported command
definitions rather than adding a root option that appears on unrelated
commands. Text remains the default. Map service results to CLI-owned DTOs and
serialize with `System.Text.Json` using explicit camel-case property names.
Do not serialize `ProjectLockFile` or other domain records directly: those
types evolve for persistence and implementation needs, not as public output
contracts.

The shared envelope contains `schemaVersion`, `command`, and `result`.
`command` is `audit`, `outdated`, or `validate`. Audit also has top-level
`findings` and `summary`. Audit result arrays expose:

- `packs`: `id`, `version`, `sourcePath`, `packPath`, `dependencies`
  (`id`, `version`), and `managedFiles` (`targetPath`, `sha256`).
- `links`: `name`, `sourceName`, `resolvedCommit`, and `files` (`targetPath`,
  `status`).
- `externalSources`: `packId`, `packVersion`, `alias`, `workspaceSourceName`,
  `fingerprint`, `ref`, `resolvedCommit`, and `status`.
- `externalFiles`: the external-source fields plus `sourcePath`, `targetPath`,
  and `sha256`.

Outdated result arrays expose the current table data as structured fields:
`updates` (`id`, `currentVersion`, `latestVersion`, `reason`), `outdatedLinks`
(`name`, `reasons`), `integrityViolations` (`id`, `version`, `sourceName`,
`lockedManifestDigest`, `observedManifestDigest`), and `uncheckedOciPacks`
(`id`, `version`, `sourceName`). `remoteRefsChecked` is Boolean and false for
`--offline`. Validate result contains Boolean `valid`.

Array-valued result properties are always arrays, including when empty.
Optional source paths and unresolved observed OCI digests serialize as `null`;
`resolvedCommit` and `sourceName` are nullable where the source has no such
value. Audit status values are machine-readable tokens: `current`,
`missing-workspace-source`, `invalid-workspace-source`,
`configuration-drift`, `missing-target`, `modified-target`, and
`conflicting-owner`. Map existing link statuses `ok`, `missing`, `modified`,
and `conflicting` to those tokens. Finding messages remain human-readable and
are not stable identifiers.

### Keep structured stdout separate from diagnostics

Use the invocation's output writer for the JSON serializer and its error writer
for human-readable diagnostics. Resolve format and configure invocation-scoped
output routing before the first `CliConsole` write, including startup debug
messages. In JSON mode, route `CliConsole` messages and progress to stderr; JSON
branches bypass tables and other text renderers. Preserve the existing console
path in text mode. Keep markup and ANSI out of the JSON output stream. Provide
an injectable error writer alongside the existing output writer for stream
separation tests.

Build each command result fully in memory before writing JSON. If an operation
fails after JSON mode is selected, write one envelope with `result: null` and
send the existing error text to stderr. Audit failures also include an empty
`findings` array and zero-valued summary. Validation issues are a completed
validation result (`valid: false`) and remain human-readable on stderr.
Parser-level format errors write to stderr and produce no stdout.

This avoids partial JSON when audit or outdated fails after collecting only
some of its data. It also keeps existing exit codes authoritative: audit
findings do not change success status, outdated retains its OCI-integrity
failure status, and invalid validation remains non-success.

### Map audit findings at the CLI boundary

Translate only non-current external-source, external-file, and link-file
states into findings. Use the codes and severities in the spec. The subject
shape is:

- External source: `kind: "external-source"`, `id` is the alias, `packId` is
  the owning pack.
- External file: `kind: "external-file"`, `id` is the alias, `packId` is the
  owning pack, and `path` is the project-relative target.
- Link file: `kind: "link-file"`, `id` is the link name, and `path` is the
  project-relative target.

Map `AuditService` states and `LinkFileAuditStatus` values in one mapper rather
than scattering code strings through handlers. Order audit data and findings by
stable pack/link identifiers and target paths. Compute `totalFindings` and all
three severity counts from the final findings array; initialize absent
severities to zero. Severity is informational and does not determine exit code.

### Treat v1 compatibility as an explicit public decision

Within schema version 1, adding fields and finding codes is compatible.
Removing or renaming a field or code, or changing its type or meaning, requires
a new schema version. The text format remains the default and is not part of the
JSON version lifecycle. Record the compatibility policy in the developer
reference and a new architecture decision record; keep implementation detail
out of the public guide.

## Risks / Trade-offs

- **Existing console output can contaminate stdout** → Route all JSON-mode
  console messages and progress to the invocation error writer; test both
  streams, including failures.
- **A late failure can leave partial JSON** → Collect before serializing and
  emit the failure envelope exactly once.
- **Internal model changes can accidentally break consumers** → Use explicit
  DTOs and compatibility tests for field names, types, and status values.
- **Finding mappings can drift from audit states** → Keep one mapper with
  focused tests for every status-to-code and severity mapping.
- **Stable codes can be mistaken for process status** → Preserve existing exit
  behavior and document that finding severity does not control it.

## Migration Plan

No persisted data migration is needed. Ship the format as an opt-in additive
CLI feature: existing invocations remain text, while consumers can adopt
`--format json` and `schemaVersion: 1` independently. If implementation must be
rolled back, removing the opt-in flag restores prior behavior without changing
project files or lock state.
