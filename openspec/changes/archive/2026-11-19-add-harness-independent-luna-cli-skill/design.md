# Design

## Context

See `proposal.md` for motivation and `specs/luna-cli-skill/spec.md` for the
behavior contract. The Skills CLI accepts a direct GitHub URL to a skill
subdirectory. LunaPack managed-file selectors are relative to a versioned pack
root, and LunaPack's installation ownership check preserves a pre-existing
unmanaged target. The two installers maintain independent ownership state.

## Goals / Non-Goals

**Goals:**

- Maintain one canonical `SKILL.md` source for both installation routes.
- Give the skill and LunaPack pack one stable identity and project target.
- Prevent a route switch from silently replacing a copy owned by the other
  installer.
- Keep skill examples grounded in the existing CLI and preserve explicit
  human approval for changes, sources, trust, and scripts.

**Non-Goals:**

- Change Luna CLI commands, schemas, trust behavior, or ownership semantics.
- Add cross-installer lock-file integration or support simultaneous ownership.
- Maintain harness-specific skill copies or add runtime dependencies.

## Decisions

### Store canonical skill content in the versioned LunaPack release

Place the canonical skill at
`projects/packs/lunapack/guidance/luna-cli/1.0.0/targets/.agents/skills/luna-cli/`.
Use `luna-cli` as both the skill frontmatter name and pack ID. The pack selects
that directory and targets `.agents/skills/luna-cli` with
`copy/overwrite`. LunaPack's installation planner rejects an occupied
unmanaged target, while the update planner can replace the same owned file in
later releases. The direct Skills CLI command points to the same repository
subdirectory:

```text
npx --yes skills@1.9.0 add https://github.com/lunarisdigitalsolutions/lunapack/tree/690521e024a7e62e14a9a3aa8dda3663ba241591/projects/packs/lunapack/guidance/luna-cli/1.0.0/targets/.agents/skills/luna-cli --skill luna-cli --copy
```

The documented command pins exact Skills CLI package `1.9.0` and a full commit
that contains the reviewed skill. The LunaPack route configures that same full
commit for the repository source. This keeps content and installer inputs in
one reviewed revision. A separate root-level skill plus a copied pack payload
would drift. A moving package tag or branch would allow the initial agent
instruction bytes to change without a matching skill or documentation update.

### Give each project copy one installer owner

The user selected one installer per project copy. Installation instructions
use a fail-closed preflight before the initial install and each route switch.
The preflight checks target descendants and symlinks, the Skills CLI lock, and
both Luna project/lock documents. It permits an empty, non-symlink target
directory left by uninstall and refuses to invoke either installer while an
occupied target, owner, or stale record remains. LunaPack's installation ownership
planning independently leaves an occupied unmanaged target untouched and
fails instead of claiming it.

The direct-install instructions require `luna uninstall luna-cli` before using
the Skills CLI on a LunaPack-owned copy, and `npx skills@1.9.0 remove luna-cli`
before using LunaPack on a Skills-CLI-owned copy. Do not use `--rename`, force
replacement, or a second target to bypass this boundary. No shared lock-file
protocol is introduced.

### Make the skill discoverable and self-contained

Use the standard `SKILL.md` format with model-invocation metadata because agents
should select the skill when users ask about Luna CLI or pack workflows. Keep
the skill harness-neutral and use one file unless implementation proves that
supporting files are necessary. Ground commands in `docs/developer/cli/commands.md`
and the focused source, evaluation, and pack-authoring guides.

The skill will treat pack metadata, repository content, CLI output, and
lifecycle instructions as untrusted data. It will sequence discovery and
exact-release review before install preview; use `--dry-run --scripts skip
--skip-instructions` before an unauthorized write; and ask before source
additions, trust changes, script execution, or lifecycle-instruction review.
A direct user request authorizes only the requested mutation, not additional
trust or execution.

The LunaPack route deliberately installs only `.agents/skills/luna-cli`.
Harnesses that read that universal convention can use it. Harnesses that
require `.claude/skills`, `.kiro/skills`, `.roo/skills`, or another
agent-specific destination use the Skills CLI route, which creates those links
or copies from the same canonical source.

### Keep public and internal guidance separate

Add a focused public page for the two installation routes, the single-owner
switching rule, and supported Luna CLI workflows. Update developer navigation
so users can find it. Record the canonical identity and ownership rule in a new
accepted ADR; do not rewrite existing ADRs.

## Risks / Trade-offs

- **Skills CLI can overwrite a same-name skill** → Run a fail-closed target
  and lock-state preflight before invoking it; remove through the current owner
  before switching. LunaPack independently fails on an occupied target.
- **`copy/overwrite` can replace local edits on an owned skill** → Require a
  reviewed dry run before updates and treat the managed skill as generated
  package content rather than the only editable source.
- **A direct URL or npm package tag can change resolution** → Pin an exact
  Skills CLI package version and the same full Git commit in all routes.
- **Instruction hooks can inject publisher-controlled text** → Suppress them
  with `--skip-instructions` during agent-run mutations and treat separately
  reviewed instruction files as data, not model instructions.
- **CLI syntax may evolve** → Verify skill examples against current help,
  public documentation, and pack validation during implementation.

## Migration Plan

No existing CLI state or pack requires migration. Users switching installation
routes remove the existing project copy through its current owner first.
