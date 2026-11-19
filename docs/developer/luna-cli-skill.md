# Install the Luna CLI skill

Install one harness-independent `luna-cli` agent skill from a direct Skills
CLI source or from the versioned LunaPack release. Both routes install the same
canonical content at `.agents/skills/luna-cli`.

## Prerequisites

- Run the commands from the project where the skill should be available.
- For the direct route, install Node.js so `npx` is available.
- For the LunaPack route, [install Luna](installation.md) and use a project
  with access to the LunaPack repository catalog.
- Review the destination first. One installer must own each project copy of
  `luna-cli`.

## Install with the Skills CLI

Add the canonical skill subdirectory directly:

```powershell
npx skills add https://github.com/lunarisdigitalsolutions/lunapack/tree/main/projects/packs/lunapack/guidance/luna-cli/1.0.0/targets/.agents/skills/luna-cli
```

Select the agents that should receive this project's skill links. The Skills
CLI records the canonical project copy below `.agents/skills/luna-cli` and
tracks its ownership in `skills-lock.json`.

Replace `main` with a reviewed commit SHA or release ref when the installation
must pin an immutable source. List project skills after installation:

```powershell
npx skills list
```

## Install with LunaPack

Initialize an unconfigured project when necessary, then inspect the configured
sources:

```powershell
luna init
luna sources list
```

Skip `luna init` when the project already has `lunapack.yml`. If the catalog is
not configured, add the public repository as a Git source:

```powershell
luna sources add github repository-packs `
  lunarisdigitalsolutions/lunapack `
  --ref main `
  --path projects/packs
```

Adding a source is a project configuration change. Review the source identity
and ref before running that command. Use a reviewed commit SHA instead of
`main` when the source identity must be immutable.

Inspect and validate the exact release, then preview its managed-file plan with
scripts disabled:

```powershell
luna inspect luna-cli@1.0.0
luna validate luna-cli@1.0.0
luna install luna-cli@1.0.0 --dry-run --scripts skip
```

After reviewing the plan, install the skill:

```powershell
luna install luna-cli@1.0.0 --scripts skip
```

Verify LunaPack ownership and the installed target:

```powershell
luna audit
```

The audit should list `luna-cli@1.0.0` and
`.agents/skills/luna-cli/SKILL.md`.

## Switch installation routes safely

Do not install the second route over a copy owned by the first. The installers
maintain independent lock state, and `--rename` or a second target would create
a conflicting skill rather than transfer ownership.

To move from a Skills CLI installation to LunaPack:

```powershell
npx skills remove luna-cli
luna install luna-cli@1.0.0 --scripts skip
```

To move from LunaPack to the Skills CLI:

```powershell
luna uninstall luna-cli --scripts skip
npx skills add https://github.com/lunarisdigitalsolutions/lunapack/tree/main/projects/packs/lunapack/guidance/luna-cli/1.0.0/targets/.agents/skills/luna-cli
```

If the target already exists but neither `npx skills list` nor `luna audit`
reports ownership, investigate it before either installer removes or replaces
it.

## Use the skill safely

The skill guides agents through pack discovery and evaluation, source setup,
pack authoring and validation, and installed-pack maintenance. It keeps
read-only inspection separate from project-changing commands and requires
approval before source additions, persistent trust changes, or lifecycle
script execution.

The skill remains guidance. It does not trust a pack or source, authorize a
project mutation, or make lifecycle scripts safe. Continue to review exact
versions and install plans, then use [Evaluate a pack before
installation](evaluate-packs.md), [Add a pack source](sources.md), the
[pack guide](packs/index.md), and [Lifecycle hooks and script
trust](cli/trust-and-scripts.md).
