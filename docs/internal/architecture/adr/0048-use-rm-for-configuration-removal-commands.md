---
status: accepted
date: 2026-08-26
decision-makers: [LunaPack maintainers]
---

# Use `rm` For Configuration-Removal Commands

## Context and Problem Statement

Luna commands for removing persisted project configuration used inconsistent
names: `variables rm`, `remap rm`, and `sources remove`. This makes the CLI
harder to scan, remember, and document. The convention applies to command
names, not implementation method names or user-facing prose.

## Decision Drivers

- Consistent command discovery and completion.
- Concise names for common configuration operations.
- Clear distinction between removing configuration and uninstalling packs.

## Considered Options

- Use `rm` for all configuration-removal subcommands.
- Use `remove` for all configuration-removal subcommands.
- Allow each command group to choose independently.

## Decision Outcome

Chosen option: "Use `rm` for all configuration-removal subcommands," because it
matches existing `variables rm` and `remap rm` commands while keeping command
groups consistent.

### Consequences

- New configuration-removal commands use `rm`; existing command groups migrate
  to it when their public syntax changes.
- `uninstall` remains the verb for removing installed pack roots and managed
  content; `revoke` remains the verb for withdrawing trust.
- A renamed public command is documented as an externally observable breaking
  change and covered by CLI help contract tests.

### Confirmation

CLI help contract tests verify each documented command path. Command reviews
check new configuration-removal syntax against this record.

## Pros and Cons of the Options

### Use `rm` for all configuration-removal subcommands

- Good, because command groups share one concise, recognizable verb.
- Bad, because users of a prior `remove` command must update scripts.

### Use `remove` for all configuration-removal subcommands

- Good, because the verb is self-explanatory.
- Bad, because it requires changing more established command groups.

### Allow each command group to choose independently

- Good, because it avoids migration work.
- Bad, because inconsistent syntax creates avoidable documentation and recall
  cost.

## More Information

This decision supersedes no earlier ADR. It changes source removal described in
[ADR-0047](0047-retain-lock-evidence-after-source-removal.md), but not its
state-retention decision.
