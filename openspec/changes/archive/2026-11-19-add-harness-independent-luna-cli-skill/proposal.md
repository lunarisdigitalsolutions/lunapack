# Proposal

## Why

Agents need reusable, permission-aware guidance for common Luna CLI workflows;
general CLI documentation does not provide an agent-ready process. Separate
skill copies for direct installation and LunaPack would drift and create
unclear project-file ownership.

## What Changes

- Add one harness-independent `luna-cli` skill covering pack discovery and
  evaluation, source setup, pack authoring and validation, and maintenance.
- Publish one canonical skill directory as a versioned LunaPack release and
  expose that same directory through an exact package/repository revision to
  direct `npx skills` installation.
- Use `luna-cli` as shared skill and pack identity and
  `.agents/skills/luna-cli` as project target. One installer owns each project
  copy; fail-closed preflight checks both lock formats and users remove it
  through its current owner before switching routes. LunaPack supports
  harnesses that read the universal `.agents/skills` convention; other
  harnesses use the direct Skills CLI route for agent-specific destinations.
- Include human-approval boundaries for project mutations, source additions,
  lifecycle scripts and instructions, trust changes, and install previews.
- Document both installation routes and record the durable identity and
  ownership decision. Do not change Luna CLI behavior.

## Capabilities

### New Capabilities

- `luna-cli-skill`: Provide a portable Luna CLI agent skill through direct
  Skills CLI and LunaPack installation.

### Modified Capabilities

None.

## Impact

Adds a skill and versioned pack under `projects/packs/lunapack/`, public
installation guidance under `docs/developer/`, and an accepted ADR under
`docs/internal/architecture/adr/`. No CLI commands, schemas, APIs, or runtime
dependencies change.
