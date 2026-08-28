---
status: accepted
date: 2026-08-28
decision-makers: [LunaPack maintainers]
---

# ADR-0057: Treat ignore remaps as unowned exclusions

## Context and Problem Statement

Consumer repositories sometimes cannot accept a file or directory supplied by
a pack or link. Normal remapping relocates content but still installs and owns
it. Consumers need a reusable exclusion that participates in the same file and
directory precedence rules without creating misleading lock ownership.

Updates also need deterministic behavior when exclusion policy changes. Adding
an exclusion must not overwrite or delete existing local content, while
removing one must allow previously omitted content to become managed.

## Decision Drivers

- Express exclusions through existing project and install remapping surfaces.
- Keep lock ownership aligned with files Luna currently manages.
- Preserve local content when policy stops Luna from managing a target.
- Allow exact file exceptions below an excluded directory.
- Avoid a second exclusion syntax with different precedence rules.

## Considered Options

- Add separate file and directory exclusion collections.
- Treat `@ignore` as a reserved remapping target.
- Record ignored files as managed lock entries without writing them.

## Decision Outcome

Chosen option: "Treat `@ignore` as a reserved remapping target", because it
reuses remap parsing, precedence, configuration persistence, and target
selection while making exclusion intent explicit.

An exact, case-sensitive `@ignore` mapping value suppresses its declared file
or every concrete file below its declared directory. Exact file mappings retain
their existing precedence over directory mappings and can therefore preserve
or relocate selected descendants of an ignored directory.

Install and update plans omit ignored files. New ignored files are not written
and receive no lock entry. When an existing managed target becomes ignored,
Luna leaves its local content unchanged and removes ownership from the next
lock. Removing an ignore mapping allows a later update to install content that
was previously absent.

### Consequences

- Good, because configuration and invocation remaps share one exclusion model.
- Good, because lock files contain only active ownership evidence.
- Good, because adding an ignore mapping cannot delete local content.
- Bad, because `@ignore` can no longer name a literal remap destination.
- Bad, because an existing preserved file is unmanaged after exclusion and may
  require normal conflict handling if management is later restored.

### Confirmation

Unit and process tests cover file and directory exclusion, file precedence,
saved install remaps, omitted lock entries, preserving existing files on
update, and installing previously omitted files after removal.

## More Information

Extends [ADR-0036](0036-record-declared-and-effective-managed-targets.md) and
[ADR-0051](0051-normalize-links-into-managed-root-lifecycle.md).
