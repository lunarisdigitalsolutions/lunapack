# Changelog

This file records externally observable changes for LunaPack consumers. Exclude
internal maintenance work, such as CI, build, or release-process changes.

## Unreleased

### Pack Authoring

- New `luna pack` commands initialize, inspect, validate, and incrementally
  maintain managed content, composite references, lifecycle scripts, parameters,
  tags, and metadata in local `pack.yml`.
- Pack manifests require ID, semantic version, non-empty author, and non-empty
  license. `luna pack init` accepts `--author` and `--license`, prompting for
  missing values interactively. Discovery and search exclude manifests missing
  either attribution value.
- Pack manifests support optional non-empty `name` and absolute HTTP or HTTPS
  `homepage` metadata.

### New Packs

- Added `commitlint`, `github-actions-commitlint`, and
  `github-actions-pr-gate` packs for conventional pull request titles, Azure
  Boards references, and external-check gating workflows.
- `github-actions-pr-gate` supports configurable case-insensitive check-name
  fragments for checks excluded from gate evaluation.

### CLI Workflow

- `luna sources rm <name>` is the canonical, concise configuration-removal
  command, with `remove` retained as a compatibility alias; it now refuses to
  remove a source still used by an installed pack or its external content,
  and otherwise clears source configuration and bound project trust while
  retaining installed pack state, provenance, and managed files.
- `luna sources add git` and `luna sources add github` resolve a supplied
  short branch or tag name to its complete ref through `git ls-remote`,
  reject an ambiguous short name, and reject a repository that canonicalizes
  to an already-configured source under a different name, URL form, or
  casing.
- `luna sources add github` now requires `--ref`.
- `luna sources rename <current-id> <new-id>` renames a configured source and
  atomically updates its bound trust and lock-file references.
- Running `luna` without a subcommand now summarizes workspace maturity and
  recommends the next setup, catalog, or lifecycle commands.
- Successful core commands and recoverable missing-prerequisite or pack lookup
  failures now include up to three contextual command recommendations.
- `luna --suppress-next-steps` suppresses contextual next-step recommendations for any
  command.

## Version 1.0.0 - 2026-08-25

LunaPack 1.0.0 is the first public release for managing reusable, versioned
engineering packs in a consumer workspace.

### CLI And Catalog

- `luna init` creates version-1 `lunapack.yml` and `lunapack-lock.yml` files.
- `luna sources add local`, `luna sources add git`, and `luna sources list`
  configure and inspect local or Git pack catalogs. Git sources support a ref
  and a repository-relative pack path.
- `luna discover` and `luna search` show the latest release by default in
  separate Pack and Version columns. Both accept `--versions` (`-v`) from one
  through 10 to display recent distinct releases per pack.
- `luna validate <id>[@<version>]` reports manifest and selected-source-file
  issues. `luna inspect <id>[@<version>]` shows selected pack metadata,
  parameters, and references. Discovery and search isolate invalid candidates
  in otherwise usable sources.
- `luna variables list`, `set <name> <value>`, and `rm <name>` manage project
  variables. `luna remap list`, `set`, and `rm` manage target remapping.
- Every command supports `--workspace`; command options provide documented
  shorthand aliases and completion.
- `--log-level` (`-ll`) selects lower-case Spectre.Console output levels.
  Diagnostics use distinct prefixes, long operations show progress, and
  interactive decisions use console prompts.

### Pack Lifecycle

- `luna install` resolves latest or exact pack releases. It supports multiple
  ordered references, dry runs, project-relative destinations, parameters,
  variables, conditions, templates, matching-file adoption, and composite
  dependencies.
- Install warns and skips already-installed roots while continuing remaining
  references. Missing explicit versions suggest the latest available version.
- `luna outdated` identifies roots with available updates. `luna update`
  updates one root or all roots, supports dry runs, and can prompt for roots
  during an all-pack update.
- `luna uninstall` conservatively removes unchanged, unshared managed content.
  Missing targets warn without blocking ownership cleanup; line and JSON merge
  targets remain in place.
- `luna mv` relocates one uniquely owned managed file and can rebind ownership
  after a matching manual move. `luna audit` reports resolved packs, source
  provenance, dependencies, and managed-file ownership.
- Pack manifests can declare `preInstall`, `postInstall`, `preUpdate`, and
  `postUpdate` hooks. Install and update accept `--scripts prompt|run|skip`;
  prompt mode requires source-specific trust or interactive consent, while run
  mode explicitly authorizes hooks for that invocation.
- Lifecycle script arguments are strict Scriban templates over resolved pack
  parameters. Luna renders each argument before dry-run output, authorization,
  and execution while preserving one manifest item as one process argument.
- Lifecycle writes and Luna-managed project state are transactional. SHA-256
  ownership checks protect consumer edits and immutable operation snapshots
  protect approved script inputs.

### Pack Authoring And Formats

- Version-1 schemas define project configuration, complete resolved lock state,
  and pack manifests. Paths accept either separator at input boundaries and
  persist with forward slashes.
- Lock files record exact dependency graphs, configured source identities,
  immutable source provenance, declared and effective targets, parameters,
  strategies, and installed-content digests.
- Packs support exact-version dependencies, composites, referenced-parameter
  bindings, source files, directories, globs, conditions, UTF-8 Scriban
  templates, and string, Boolean, or enum parameters.
- Managed files support copy, line merge, section merge, and JSON merge
  strategies. Shared targets require compatible merge strategies.
- Pack manifests require non-empty license and author attribution, accept up to
  15 searchable tags, and require `template: true` before Scriban rendering.

### Distribution

- Luna ships self-contained Native AOT archives for Windows x64, Linux x64,
  Linux Arm64, macOS x64, and macOS Arm64; the
  `@lunarisdigitalsolutions/lunapack` npm launcher with matching platform
  packages; the `Lunaris.Lunapack.Luna` .NET tool package; and a Linux x64 OCI
  image.
- Distribution packages expose the `luna` executable. Archive checksums and
  package provenance support artifact verification.

### Bundled Packs

- The repository includes reusable packs for .NET projects, .NET SDK 10,
  EditorConfig, build properties, central package management, CSharpier,
  `.gitignore`, MIT licensing, clean-code guidance, C# guidance, and MADR ADR
  templates.

### Developer Documentation

- A browsable documentation site provides installation, CLI reference, pack
  authoring, architecture, troubleshooting, release, trust, and threat-model
  guidance.
