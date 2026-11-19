---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0090: Version the CLI JSON Diagnostic Contract

## Context and Problem Statement

Automation depends on Luna command output, but human-oriented tables and
messages are presentation details rather than stable integration contracts.
Audit also needs stable identifiers for findings without changing the existing
text output or command exit behavior. The CLI therefore needs a
machine-readable contract that can evolve without silently breaking consumers.

## Decision Drivers

- Preserve the existing text output as the default interface.
- Keep standard output parseable while retaining human diagnostics on standard
  error.
- Separate durable public fields from internal lock, catalog, and service
  models.
- Let consumers adopt stable finding identities without depending on message
  wording.
- Define which JSON changes are compatible before the contract reaches users.

## Considered Options

- Keep unstructured text as the only automation interface.
- Add command-specific, unversioned JSON objects mapped directly from internal
  records.
- Add explicit command DTOs behind one versioned JSON envelope.

## Decision Outcome

Chosen option: "Add explicit command DTOs behind one versioned JSON envelope,"
because it gives automation a stable boundary while allowing internal models
and text presentation to continue evolving independently.

The shared envelope carries integer `schemaVersion`, `command`, and
`result`. Root `audit`, `outdated`, and `validate <pack-reference>` opt in with
`--format json`; omitted format remains `text`. Audit additionally carries
`findings` and `summary`. JSON serialization uses CLI-owned DTOs rather than
lock-file, catalog, or service result types. Every supported command emits a
complete object after collecting its result, including `result: null` when the
operation cannot produce one.

The structured mode preserves the original console's input and interactive
capability while redirecting diagnostics. Control characters and terminal
escape input are neutralized at the diagnostic writer. JSON help and parse
errors remain on standard error, while unsupported formats fail before help or
command action dispatch. Online outdated checks fail closed if any configured
source or OCI discovery is incomplete; `remoteRefsChecked: true` therefore
means every required remote check completed.

Within schema version 1, adding fields and finding codes is compatible.
Removing or renaming a field or code, or changing its type or meaning, requires
a new schema version. Finding `code` and subject identity are stable; message
wording is not. Finding severity remains separate from process exit semantics.

### Consequences

- Automation can parse stdout independently of terminal presentation and
  diagnostic text.
- Explicit DTOs require a review and mapping update when public output changes.
- Source-generated serialization keeps the contract compatible with Luna's
  Native AOT builds.
- JSON mode keeps prompt semantics while neutralizing terminal controls and
  separating machine data from diagnostics.
- A source outage cannot produce a success-shaped empty outdated result.
- Audit-first finding codes establish the diagnostic vocabulary without
  requiring codes for every command in the same release.
- A future incompatible change must introduce and document a new schema version
  rather than changing version 1 in place.

### Confirmation

Unit and CLI application tests confirm envelope names and types, null and empty
values, stream separation, parser and operation failures, every audit status
mapping, finding counts, outdated offline evidence, and validation success and
failure. Release build and OpenSpec validation cover AOT-compatible
serialization and the accepted public contract.

## Pros and Cons of the Options

### Keep unstructured text

- Good, because no new output model is required.
- Bad, because scripts remain coupled to presentation wording and terminal
  layout.

### Add unversioned command JSON from internal records

- Good, because the initial implementation is smaller.
- Bad, because persistence and service-model changes silently become public
  breaking changes.
- Bad, because consumers have no explicit migration signal.

### Add explicit DTOs behind a versioned envelope

- Good, because compatibility is explicit and command data is intentionally
  mapped.
- Good, because commands share stream and serialization behavior.
- Bad, because every new public field requires a deliberate DTO and test
  update.

## More Information

This decision implements
[issue #75](https://github.com/lunarisdigitalsolutions/lunapack/issues/75) and
the
[`stable-machine-readable-cli-diagnostics` OpenSpec change](../../../../openspec/changes/stable-machine-readable-cli-diagnostics/proposal.md).
It follows [ADR-0030](0030-use-spectre-console-for-cli-output.md) for text
presentation and [ADR-0074](0074-evolve-yaml-schemas-compatibly.md) for the
repository's additive compatibility model.
