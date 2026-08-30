# Architecture Decision Records

Create a new architecture decision record from the [MADR-based template](template.md). Add every accepted record to this index with a short decision summary.

| ADR                                                                             | Decision                                                                                |
| ------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| [ADR-0001](0001-organize-documentation-by-audience.md)                          | Organize documentation by its product, internal, developer, or pitch audience.          |
| [ADR-0002](0002-enforce-repository-documentation-quality.md)                    | Enforce Markdown linting and structured-file formatting through local quality gates.    |
| [ADR-0003](0003-govern-changes-with-openspec.md)                                | Use OpenSpec to govern planned capability changes and their documentation impact.       |
| [ADR-0004](0004-adopt-version-1-local-manifests.md)                             | Adopt version-1 local manifests and schemas.                                            |
| [ADR-0005](0005-protect-local-managed-files.md)                                 | Protect user-owned local managed files.                                                 |
| [ADR-0006](0006-establish-dotnet-quality-baseline.md)                           | Establish the .NET testing and quality baseline.                                        |
| [ADR-0007](0007-standardize-dotnet-build-and-schema-tooling.md)                 | Standardize the .NET build and schema-validation toolchain.                             |
| [ADR-0008](0008-abstract-filesystem-access.md)                                  | Abstract filesystem access for isolated unit testing.                                   |
| [ADR-0009](0009-adopt-madr-adr-template.md)                                     | Adopt MADR as the repository ADR template.                                              |
| [ADR-0010](0010-adopt-repository-local-code-review-agent.md)                    | Provide a repository-local Copilot agent for evidence-based code reviews.               |
| [ADR-0011](0011-use-local-csharpier-tool.md)                                    | Use a repository-local CSharpier tool for staged C# formatting.                         |
| [ADR-0012](0012-use-scoped-copilot-coding-instructions.md)                      | Use scoped Copilot instructions linked to maintainer coding guidance.                   |
| [ADR-0013](0013-adopt-system-command-line.md)                                   | Use System.CommandLine for CLI syntax and parser validation.                            |
| [ADR-0014](0014-adopt-source-dispatched-pack-catalog.md)                        | Use source-specific browsing and deterministic Semantic Versioning resolution.          |
| [ADR-0015](0015-install-bundled-engineering-packs.md)                           | Install bundled packs and restore repository-specific overlays manually.                |
| [ADR-0016](0016-split-portable-configuration-from-resolved-lock-state.md)       | Split portable configuration from resolved lock state.                                  |
| [ADR-0017](0017-bind-pack-parameters-before-rendered-ownership.md)              | Bind graph parameters once and record rendered content as ownership.                    |
| [ADR-0018](0018-plan-strategy-aware-pack-updates.md)                            | Plan strategy-aware pack update before a single transactional commit.                   |
| [ADR-0019](0019-use-installed-git-for-pack-sources.md)                          | Use installed Git with immutable cache and lock provenance for pack sources.            |
| [ADR-0020](0020-use-plural-sources-cli-command.md)                              | Use a plural source-management command and list configured sources.                     |
| [ADR-0021](0021-prioritize-composite-root-parameter-contracts.md)               | Prioritize root contracts and bind hidden transient parameters.                         |
| [ADR-0022](0022-adopt-audience-local-diataxis-documentation.md)                 | Organize current documentation by audience and Diataxis purpose.                        |
| [ADR-0023](0023-validate-cli-locks-before-runtime-builds.md)                    | Validate all CLI runtime lock data before runtime-specific builds.                      |
| [ADR-0024](0024-publish-developer-docs-with-docusaurus.md)                      | Publish canonical developer docs through Docusaurus and GitHub Pages.                   |
| [ADR-0025](0025-separate-pitch-decks-by-audience.md)                            | Publish separate business and technical LunaPack pitch decks.                           |
| [ADR-0026](0026-name-the-lunapack-cli-luna.md)                                  | Name LunaPack's CLI and consumer release assets Luna.                                   |
| [ADR-0027](0027-require-explicit-template-rendering.md)                         | Require explicit template rendering for managed files.                                  |
| [ADR-0028](0028-isolate-invalid-pack-candidates.md)                             | Isolate invalid pack candidates from catalog browsing.                                  |
| [ADR-0029](0029-provide-cli-option-shorthand-aliases.md)                        | Provide stable, command-scoped shorthand aliases for CLI options.                       |
| [ADR-0030](0030-use-spectre-console-for-cli-output.md)                          | Use Spectre.Console for CLI output and diagnostics.                                     |
| [ADR-0031](0031-require-pack-attribution-and-interactive-parameters.md)         | Require pack attribution and interactive parameter metadata.                            |
| [ADR-0032](0032-apply-lifecycle-roots-incrementally.md)                         | Apply lifecycle roots incrementally while preserving compatible lock entries.           |
| [ADR-0033](0033-add-optional-pack-classification-tags.md)                       | Add bounded optional tags for pack catalog classification.                              |
| [ADR-0034](0034-default-catalog-results-to-latest-release.md)                   | Default catalog results to the latest release with bounded history overrides.           |
| [ADR-0035](0035-skip-installed-roots-in-multi-pack-installs.md)                 | Skip installed roots in multi-pack installs and provide version guidance.               |
| [ADR-0036](0036-record-declared-and-effective-managed-targets.md)               | Record declared and effective managed targets and require explicit relocation.          |
| [ADR-0037](0037-canonicalize-persisted-project-paths.md)                        | Canonicalize persisted project and lock paths with forward slashes.                     |
| [ADR-0038](0038-distribute-luna-through-github-npm-and-nuget.md)                | Distribute Luna through GitHub Releases, npm, and NuGet.                                |
| [ADR-0039](0039-enforce-native-aot-cli-builds.md)                               | Enforce Native AOT publishing with typed manifest validation.                           |
| [ADR-0040](0040-secure-lifecycle-scripts-with-scoped-trust.md)                  | Secure lifecycle scripts with scoped trust and explicit residual risks.                 |
| [ADR-0041](0041-use-ephemeral-release-identities.md)                            | Use ephemeral identities for release publication.                                       |
| [ADR-0042](0042-exclude-build-metadata-from-release-tags.md)                    | Exclude SemVer build metadata from cross-registry Luna release tags.                    |
| [ADR-0043](0043-verify-existing-release-assets-on-rerun.md)                     | Verify immutable GitHub Release assets and notes before resuming a rerun.               |
| [ADR-0044](0044-render-lifecycle-script-arguments.md)                           | Render lifecycle script arguments before trust authorization and execution.             |
| [ADR-0045](0045-separate-open-source-access-from-services.md)                   | Keep open-source access separate from optional Lunaris commercial services.             |
| [ADR-0046](0046-centralize-contextual-cli-guidance.md)                          | Centralize workspace-aware recommendation selection and rendering.                      |
| [ADR-0047](0047-retain-lock-evidence-after-source-removal.md)                   | Retain installed lock evidence while revoking trust after source removal.               |
| [ADR-0048](0048-use-rm-for-configuration-removal-commands.md)                   | Use `rm` consistently for configuration-removal subcommands.                            |
| [ADR-0049](0049-separate-pack-authoring-validity-from-publication-readiness.md) | Separate incremental manifest validity from publication readiness.                      |
| [ADR-0050](0050-require-pack-attribution-for-catalogs.md)                       | Require author and license attribution for pack manifests and catalogs.                 |
| [ADR-0051](0051-normalize-links-into-managed-root-lifecycle.md)                 | Normalize project-owned links into the shared managed-root lifecycle.                   |
| [ADR-0052](0052-inherit-terminal-for-interactive-lifecycle-hooks.md)            | Inherit terminal streams for lifecycle hooks in interactive sessions.                   |
| [ADR-0053](0053-unify-ordered-lifecycle-hooks.md)                               | Superseded by ADR-0055.                                                                 |
| [ADR-0054](0054-use-semantic-cli-presentation-and-explicit-defaults.md)         | Use semantic CLI presentation and explicit safe defaults.                               |
| [ADR-0055](0055-extend-recoverable-lifecycle-hooks-through-uninstall.md)        | Extend recoverable lifecycle hooks through uninstall.                                   |
| [ADR-0056](0056-normalized-source-fingerprint-authority.md)                     | Normalize source identity into one fingerprint authority for alias mapping and removal. |
| [ADR-0057](0057-resolve-template-paths-from-installation-plans.md)              | Resolve managed-file template paths from condition-selected installation plans.         |
| [ADR-0058](0058-represent-multi-select-enums-as-ordered-arrays.md)              | Represent multi-select enums as ordered unique arrays validated at graph boundaries.    |
| [ADR-0059](0059-treat-ignore-remaps-as-unowned-exclusions.md)                   | Treat `@ignore` remaps as unowned exclusions that preserve existing local files.        |
| [ADR-0060](0060-make-script-denial-dominant.md)                                 | Make script denial dominant and monotonic across every applicable trust scope.          |
| [ADR-0061](0061-separate-release-builds-from-instrumented-tests.md)             | Separate strict Release builds from instrumented test coverage.                         |
| [ADR-0062](0062-organize-cli-source-by-feature.md)                              | Organize CLI source by feature with matching namespaces.                                |
| [ADR-0063](0063-organize-cli-tests-by-feature-and-scenario.md)                  | Organize CLI tests by feature and broader tests by scenario.                            |
| [ADR-0064](0064-ground-pack-examples-in-testable-catalogs.md)                   | Separate maintained consumer examples from synthetic pack-authoring workflows.          |
| [ADR-0065](0065-organize-packs-by-release-and-payload-purpose.md)               | Organize immutable pack releases and group payloads by ownership purpose.               |
