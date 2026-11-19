---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0092: Keep One Canonical Luna CLI Skill

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
`copy/overwrite`. Initial installation still rejects an occupied target that
is not managed by LunaPack; the strategy lets a later release replace the same
LunaPack-owned file. The Skills CLI installation URL pins commit
`117cecc18e8c33418b8a540b2637b384441601e9` and runs exact package version
`skills@1.9.0`; the LunaPack Git source uses the same full commit. Agent
instruction installation does not follow an unpinned npm tag or a moving
repository ref.

Each project copy has one installer owner. Before an initial direct or LunaPack
installation, and before every route switch, the documented PowerShell
preflight must verify that the canonical target has no descendants or symlink,
neither `skills` lock nor Luna project/lock state contains `luna-cli`, and the
relevant installer can therefore claim the copy without conflict. The
operation fails closed when any condition is true. LunaPack's installation
ownership check independently protects an unmanaged occupied target, while
`overwrite` permits future updates to the same owned file. The Skills CLI has
no equivalent cross-installer guard, so users must not bypass or omit this
preflight.

An empty, non-symlink target directory is not an occupied target because
LunaPack uninstall may leave one behind. The preflight still refuses a target
file, symlink, any descendant, or either installer's lock record.

The installers do not share lock state. No `--rename` variant, secondary target,
cross-installer lock protocol, force replacement, or simultaneous ownership is
supported. Route switching removes the copy through its recorded owner,
repeats the preflight, and invokes only then the other installer.

The LunaPack route installs the universal `.agents/skills` copy only and is
supported for harnesses that read that convention. Harnesses requiring a
different agent-specific directory use the Skills CLI route, which creates
the canonical project copy and the selected harness links or copies from the
same skill source.

The skill treats pack metadata, repository content, CLI output, and lifecycle
instructions as untrusted data. Agent-run install, update, and uninstall
commands combine `--scripts skip` with `--skip-instructions`, because script
consent controls do not suppress publisher-controlled instruction hooks.

### Consequences

- Good, because command guidance and approval boundaries have one review and
  release source.
- Good, because either installer produces the same skill identity and project
  target without a runtime dependency.
- Good, because LunaPack's ownership planner preserves an unowned or
  Skills-CLI-owned target and reports the conflict while allowing updates to
  the LunaPack-owned skill.
- Good, because fail-closed preflight prevents a known Skills CLI owner or
  LunaPack owner from being silently replaced through the documented routes.
- Good, because exact package and repository revisions bind initial agent
  instruction bytes.
- Bad, because the installers keep separate lock state and switching routes
  requires explicit removal through the current owner.
- Bad, because the Skills CLI cannot consume LunaPack lock state itself;
  consumers must run the documented preflight immediately before installation.
- Bad, because harnesses without `.agents/skills` discovery cannot use the
  LunaPack target and must select the direct Skills CLI route.

### Confirmation

Confirm the canonical skill is accepted by the exact Skills CLI package and
passes LunaPack local manifest validation and catalog validation as
`luna-cli@1.0.0`. Smoke-test the package/repository pins and both installation
routes in separate clean projects; compare installed `SKILL.md` bytes. Verify
the ownership preflight refuses an occupied target, Skills CLI lock ownership,
and LunaPack config/lock ownership in both route directions, while accepting
the empty directory left by LunaPack uninstall. Verify LunaPack independently
preserves a Skills-CLI-owned target on initial install, updates a changed
LunaPack-owned skill through `copy/overwrite`, and permits successful switching
only after removal through the current owner.

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
