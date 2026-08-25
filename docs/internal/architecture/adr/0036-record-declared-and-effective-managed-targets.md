---
status: accepted
date: 2026-08-14
decision-makers: [LunaPack maintainers]
---

# ADR-0036: Record declared and effective managed targets

## Context and Problem Statement

Pack manifests need portable managed-file targets, while consumer repositories
often use different layouts. A target resolved from consumer policy must remain
stable for update and uninstall, even after that policy changes. Earlier lock
records stored only effective targets, making a remapped file impossible to
correlate reliably with its manifest declaration across releases.

## Decision Drivers

- Keep pack manifests portable across consumer repository layouts.
- Preserve ownership and digest protection through update and uninstall.
- Make relocation intentional, preflighted, and recoverable.
- Maintain compatibility with existing version-1 lock files.

## Considered Options

- Store only effective target paths.
- Store declared and effective target paths in versioned lock records.
- Infer relocations whenever consumer remapping changes.

## Decision Outcome

Chosen option: "Store declared and effective target paths in versioned lock
records", because declared identity correlates releases while effective identity
locates the protected project file.

`lunapack-lock.yml` schema version 1 records `declaredTargetPath` and
`targetPath` for every managed file. Lifecycle operations correlate retained
files by declared target and operate at the recorded effective target.

Global remapping is reusable policy for new installations and newly introduced
release files. It never relocates existing files. `luna mv` is the explicit,
lock-backed relocation operation; it moves a uniquely owned file or rebinds an
already moved file, and restores a filesystem move when lock persistence fails.

### Consequences

- Good, because updates and uninstalls remain deterministic after remapping.
- Good, because consumers can change policy without silent filesystem moves.
- Bad, because every lock producer must supply both target identities.
- Bad, because relocation requires an explicit command instead of configuration
  changes alone.

### Confirmation

Schema-validation and lifecycle tests cover remapped installation, update
retention, uninstall, explicit move, rebinding, ambiguity, and rollback after
state-save failure.

## More Information

Related: [ADR-0016](0016-split-portable-configuration-from-resolved-lock-state.md)
and the [project lock-file specification](../../../../openspec/specs/project-lockfile/spec.md).
