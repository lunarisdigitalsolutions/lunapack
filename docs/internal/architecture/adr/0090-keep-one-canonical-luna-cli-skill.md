---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0090: Keep One Canonical Luna CLI Skill

## Context and Problem Statement

Agents need reusable guidance for Luna CLI workflows, and consumers need to
install that guidance through either the Skills CLI or LunaPack. Maintaining a
separate copy for each installer would let command guidance, approval
boundaries, and ownership behavior drift.

The two installers also maintain independent project lock state. Installing a
second copy at the same target without transferring ownership could make the
configured state disagree with the files on disk.

## Decision Drivers

- Preserve one authoritative `luna-cli` skill across installation routes.
- Keep the skill harness-independent and useful through normal agent-skill
  discovery.
- Prevent either installer from silently claiming a copy managed by the other.
- Keep the canonical project target stable across future skill content updates.
- Avoid changing Luna CLI commands, schemas, runtime behavior, or trust
  semantics.

## Considered Options

- Maintain one canonical skill in a versioned LunaPack release and expose that
  directory to the Skills CLI.
- Maintain separate Skills CLI and LunaPack copies.
- Generate a LunaPack payload from a separate root-level skill during release.

## Decision Outcome

Chosen option: "Maintain one canonical skill in a versioned LunaPack release and
expose that directory to the Skills CLI," because both routes can consume the
same reviewed bytes without a generated copy or a CLI integration.

The canonical source for release `1.0.0` is
`projects/packs/lunapack/guidance/luna-cli/1.0.0/targets/.agents/skills/luna-cli/`.
The skill frontmatter name and LunaPack pack ID are both `luna-cli`, and both
installers target `.agents/skills/luna-cli`.

The LunaPack release selects the canonical directory with
`copy/fail-if-exists`. The Skills CLI installation URL points to that same
directory. Each project copy has one installer owner. Before switching routes,
the user removes the copy through its current installer. A direct install must
not be overwritten by LunaPack, and users must not run the Skills CLI over a
LunaPack-owned copy.

The installers do not share lock state. No `--rename` variant, secondary target,
cross-installer lock protocol, or simultaneous ownership is supported. A
moving Git ref may be used for convenient discovery, but reproducible
automation should select a reviewed commit or release ref.

### Consequences

- Good, because command guidance and approval boundaries have one review and
  release source.
- Good, because either installer produces the same skill identity and project
  target without a runtime dependency.
- Good, because LunaPack's `fail-if-exists` strategy preserves an unowned or
  Skills-CLI-owned target and reports the conflict.
- Bad, because the installers keep separate lock state and switching routes
  requires explicit removal through the current owner.
- Bad, because a direct URL using `main` follows repository movement unless the
  consumer pins another ref.

### Confirmation

Confirm the canonical skill is accepted by the Skills CLI and passes LunaPack
local manifest validation and catalog validation as `luna-cli@1.0.0`. Smoke-test
both installation routes in separate clean projects and compare the installed
`SKILL.md` bytes. Attempt LunaPack installation over a Skills-CLI-owned target
and confirm the target remains unchanged; remove it through the Skills CLI,
then confirm LunaPack installation succeeds.

## Pros and Cons of the Options

### One canonical versioned release

- Good, because content, metadata, and release review have one source.
- Good, because direct and managed installation can be compared byte for byte.
- Bad, because switching installers requires an explicit ownership transfer.

### Separate Skills CLI and LunaPack copies

- Good, because each distribution can optimize its metadata independently.
- Bad, because commands and safety guidance can drift.
- Bad, because reviewers must reconcile two release artifacts.

### Generate the LunaPack payload

- Good, because the release can retain a root-level skill authoring location.
- Bad, because the maintained source and generated payload can disagree.
- Bad, because release tooling must preserve byte equivalence and provenance.

## More Information

This decision implements
[issue #102](https://github.com/lunarisdigitalsolutions/lunapack/issues/102)
and the OpenSpec change
[`add-harness-independent-luna-cli-skill`](../../../../openspec/changes/add-harness-independent-luna-cli-skill/proposal.md).
