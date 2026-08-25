## Purpose

Provide portable first-party documentation packs for reusable C# and clean-code
guidance without carrying repository-specific operational instructions.

## ADDED Requirements

### Requirement: Provide generic guideline documentation packs

The repository SHALL provide `csharp-guidelines` and `clean-code-guidelines`
packs in its first-party local source. Each pack SHALL manage one Markdown
guideline file and SHALL be installable independently. The C# pack content
SHALL derive from `docs/internal/development/coding-guidelines/csharp.md`; the
clean-code pack content SHALL derive from
`docs/internal/development/coding-guidelines/clean-code.md`.

The packaged guidance SHALL remain portable: it SHALL retain language or
clean-code conventions and SHALL omit LunaPack-specific operational, product, and
repository-management instructions.

#### Scenario: Install a guideline pack at its default target

- **WHEN** a consumer installs either guideline pack without a destination
- **THEN** LunaPack creates that pack's single guideline document at the target
  declared by the pack

#### Scenario: Package content is portable

- **WHEN** either guideline pack is inspected before publication
- **THEN** its managed document contains reusable guidance only and no
  LunaPack-specific operational or repository-management instruction

### Requirement: Dogfood guideline documentation packs

The repository SHALL request both guideline packs from its local source and
install their documents in `docs/internal/development/coding-guidelines`.

#### Scenario: Resolve the repository's documentation packs

- **WHEN** LunaPack resolves the repository's `lunapack.yml`
- **THEN** both guideline packs resolve from `projects/packs` with effective
  targets in the coding-guidelines directory
