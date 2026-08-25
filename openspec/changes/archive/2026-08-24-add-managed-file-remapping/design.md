## Context

See [proposal.md](proposal.md) for motivation. Pack manifests retain portable
managed-file targets. The lifecycle planner currently applies a directly
requested pack's optional destination, while `lunapack-lock.yml` records only
the effective target and content digest. As a result, an update cannot reliably
associate a manifest target with a user-relocated file, and catalog inspection
does not receive remapping context.

The change crosses project configuration, resolution/planning, lock persistence,
filesystem transactions, CLI rendering, and documentation. Catalog discovery and
manifest schema remain unchanged.

## Goals / Non-Goals

**Goals:**

- Let consumers redirect manifest target directories or exact files through
  reusable configuration or one installation invocation.
- Preserve declared-to-effective target identity in lock state so update,
  uninstall, and explicit relocation are deterministic.
- Make ownership-safe relocation available without manually editing lock files.
- Show consumers the declared targets that a pack manages and applicable global
  remaps.

**Non-Goals:**

- Do not let pack authors declare consumer-specific remapping in `pack.yml`.
- Do not change catalog selection, dependency resolution, content strategies, or
  manifest source selectors.
- Do not infer moves during update. A changed remap configuration does not move
  previously installed content.
- Do not combine `--destination` with remapping, or expose an automatic
  filesystem-wide move command.

## Decisions

### Resolve canonical declared targets before lifecycle planning

Add a project configuration shape equivalent to:

```yml
remap:
  directories:
    docs/adr: docs/internal/01-architecture/decisions
  files:
    docs/adr/template.md: docs/adr/_template.md
```

Normalize all mapping inputs and manifest targets to project-relative canonical
paths before matching. Directory rules match complete path segments and retain
the suffix; file rules match exactly. Resolution order is command-line file,
global file, command-line directory, global directory, then the declared
target. The CLI accepts repeatable `from=to` values for
`--remap-directory` and `--remap-file`; these values affect only that install.
Reusable mappings are configured under `lunapack.yml`.

Configuration, lock-file, pack-manifest, and CLI path inputs accept either path
separator. State serialization writes all project configuration and lock-file
paths with forward slashes so project state remains portable across Windows and
Linux.

`luna remap set <directory|file> <target> <newTarget>` validates, normalizes,
and upserts one reusable mapping in the selected scope. `luna remap list`
renders configured mappings, while `luna remap rm <directory|file> <target>`
removes one. These commands update configuration only, so they apply to future
installation planning without moving existing lock-recorded targets.

The planner receives a remap resolver as part of its existing request/config
boundary and applies it before ownership, collision, adoption, dry-run, or
transaction planning. `--destination` and remapping are explicitly incompatible
because one relocates the entire direct root and the other matches canonical
manifest targets; combining them would leave ambiguous identities.

Alternative considered: store mappings on every requested pack. Rejected because
the requested contract defines global project mappings and a CLI mapping must
also support one-off installation without broadening later installations.

### Store declared and effective target identities in lock schema version 1

Extend each lock-managed-file record with its canonical declared manifest target
and retain `targetPath` as the effective project location. Bump the lock schema
contract and update serialization, YAML validation, and test fixtures together.
For every retained managed file, update matching uses declared target identity
and writes to the recorded effective target. New files introduced by a release
are resolved through current global remaps, then added to the new lock state.

This is a pre-public contract, so compatibility with earlier development lock
shapes is intentionally excluded. The first public lock schema starts at version
1 with both identities required, avoiding migration logic for unreleased state.

Alternative considered: use only the effective target as identity. Rejected
because file renames and remapping prevent reliable correlation during update.

### Implement `luna mv` as an ownership transaction

Add a focused command handler and lifecycle service operation that load valid
project state, normalize both paths, locate one unique managed-file lock record,
and preflight target ownership and file state. The operation has two success
paths:

1. Source exists and target does not: create target parent directories as
   needed, move the source, and persist the new effective target.
2. Source is absent and target exists: persist the new effective target without
   altering file content or the recorded digest.

All other source/target file-state combinations fail. The existing transactional
filesystem and configuration/lock persistence pattern must rollback a filesystem
move when lock persistence fails. Rebinding writes only lock state, preserving
the prior digest so existing update and uninstall safety checks continue to
protect consumer-modified content.

Alternative considered: update both `lunapack.yml` remaps and lock state during
`mv`. Rejected because one managed-file move is more specific than reusable
global policy, and updating global policy could relocate unrelated future files.

### Render inspection from manifest targets plus global configuration

Pass the validated project configuration or a remap resolver to the inspection
formatter. Add a managed-files table that renders manifest targets only, without
`source`, directory, or glob selectors. Its target cell renders either the
declared value or `declared -> effective` when global configuration changes the
value. Invocation-scoped remaps and installed lock state are intentionally not
shown because `inspect` previews a catalog manifest for the current project
policy rather than one particular installation.

Alternative considered: show lock-recorded locations in `inspect`. Rejected
because a pack can be inspected before installation and can have multiple
versions; installed-state reporting remains a separate concern.

### Document and govern the durable compatibility contract

Update developer installation guidance and the pack manifest reference with
global configuration examples, install option syntax, `luna mv`, lock-backed
update/uninstall semantics, and inspection output. Update the product pack
lifecycle requirement for consumer remapping. Add an internal ADR using the next
sequential identifier to record canonical declared/effective target identity,
the lock migration, and why configuration policy does not relocate existing
files. Do not add a changelog entry until implementation creates a released,
externally observable consumer change.

## Risks / Trade-offs

- [Two remapped packs collide at one target] -> Run existing ownership and
  filesystem conflict checks after resolution, before every mutation.
- [Interrupted move leaves content and lock divergent] -> Include the move in
  the existing transaction rollback path and commit lock state only after the
  filesystem action succeeds.
- [Global mapping changes surprise consumers during update] -> Treat existing
  lock locations as authoritative and require `luna mv` for explicit relocation.
- [Remap syntax is hard to read in shell] -> Document `from=to` examples and
  return option-specific validation errors for missing, duplicate, or unsafe
  paths.

## Migration Plan

1. Define schema version 1 with declared and effective target identities,
   configuration remap validation, and direct unit coverage.
2. Route install, dry-run, update, uninstall, inspect, and move through the
   resolver and declared/effective lock identities.
3. Validate filesystem and state rollback across collision, unsupported legacy
   records, and persistence failures.
4. Publish the developer, product, and internal ADR documentation with the
   released behavior.

Rollback is code-version rollback before publication. No compatibility promise
exists for lock files produced by unreleased development builds.
