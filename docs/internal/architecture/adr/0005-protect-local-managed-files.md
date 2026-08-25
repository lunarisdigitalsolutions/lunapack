---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0005: Protect Local Managed Files

## Context and Problem Statement

Pack installation and removal can otherwise overwrite or delete user-owned
content.

## Decision Drivers

- Prevent lifecycle operations from destroying user-owned content.
- Distinguish a managed file that remains unchanged from one a user modified.
- Make removal safe without taking ownership of pre-existing files.

## Considered Options

- Protect existing and modified managed files with ownership digests.
- Overwrite installation targets.
- Always delete managed targets on uninstall.

## Decision Outcome

Chosen option: "Protect existing and modified managed files with ownership
digests", because recorded SHA-256 values allow lifecycle operations to detect
whether a user changed a file after installation.

### Consequences

- Good, because local lifecycle operations preserve user changes.
- Bad, because users must resolve changed or missing managed targets before uninstalling a pack.

### Confirmation

Unit and integration tests cover rejected installation targets and removal of
unchanged, changed, and missing managed files.

## Pros and Cons of the Options

### Protect existing and modified managed files with ownership digests

- Good, because file ownership and modifications are explicit.
- Bad, because lifecycle operations must maintain and compare digests.

### Overwrite installation targets

- Good, because installation could always complete at the requested path.
- Bad, because it risks destroying user content.

### Always delete managed targets on uninstall

- Good, because uninstall behavior would be simple.
- Bad, because it risks deleting user modifications.

## More Information

Reject existing installation targets. Record SHA-256 digests for created files.
Remove a managed file only when its current digest matches the recorded value.
