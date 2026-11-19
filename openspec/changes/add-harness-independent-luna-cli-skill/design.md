# Design

## Context

See `proposal.md` for motivation and `specs/luna-cli-skill/spec.md` for the
behavior contract. The Skills CLI accepts a direct GitHub URL to a skill
subdirectory. LunaPack managed-file selectors are relative to a versioned pack
root, and `copy/fail-if-exists` preserves a pre-existing target. The two
installers maintain independent ownership state.

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
`copy/fail-if-exists`. The direct Skills CLI command points to the same
repository subdirectory:

```text
npx skills add https://github.com/lunarisdigitalsolutions/lunapack/tree/main/projects/packs/lunapack/guidance/luna-cli/1.0.0/targets/.agents/skills/luna-cli
```

This keeps content in one place and uses the Skills CLI's supported
subdirectory source. A separate root-level skill plus a copied pack payload
would drift. A pack-defined external Git source would require consumers to
approve and resolve another source and would separate the content ref from the
versioned pack.

### Give each project copy one installer owner

The user selected one installer per project copy. Installation instructions
will require removal through the current owner before switching. LunaPack's
`copy/fail-if-exists` strategy leaves an existing skill untouched and fails
instead of claiming or overwriting it. The direct-install instructions will
require `luna uninstall luna-cli` before using `npx skills` on a LunaPack-owned
copy, and `npx skills remove luna-cli` before using LunaPack on a
Skills-CLI-owned copy. Do not use `--rename` or a second target to bypass this
boundary. No shared lock-file protocol is introduced.

### Make the skill discoverable and self-contained

Use the standard `SKILL.md` format with model-invocation metadata because agents
should select the skill when users ask about Luna CLI or pack workflows. Keep
the skill harness-neutral and use one file unless implementation proves that
supporting files are necessary. Ground commands in `docs/developer/cli/commands.md`
and the focused source, evaluation, and pack-authoring guides.

The skill will sequence discovery and exact-release review before install
preview; use `--dry-run --scripts skip` before an unauthorized write; and ask
before source additions, trust changes, or script execution. A direct user
request authorizes only the requested mutation, not additional trust or
execution.

### Keep public and internal guidance separate

Add a focused public page for the two installation routes, the single-owner
switching rule, and supported Luna CLI workflows. Update developer navigation
so users can find it. Record the canonical identity and ownership rule in a new
accepted ADR; do not rewrite existing ADRs.

## Risks / Trade-offs

- **Skills CLI can overwrite a same-name skill** → Document removal through the
  current owner before switching; LunaPack also fails on an occupied target.
- **A direct URL can follow a moving Git ref** → Keep release directories
  versioned and immutable; recommend pinning a commit or release ref when
  reproducibility is required.
- **CLI syntax may evolve** → Verify skill examples against current help,
  public documentation, and pack validation during implementation.

## Migration Plan

No existing CLI state or pack requires migration. Users switching installation
routes remove the existing project copy through its current owner first.
