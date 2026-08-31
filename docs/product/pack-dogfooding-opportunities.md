# Pack Dogfooding Opportunities

This analysis identifies repository assets that can become reusable packs and
records which candidates are now implemented. It does not make the remaining
proposals part of LunaPack's current contract.

## Current position

The repository catalog currently contains 45 packs under `projects/packs`. Its
`lunapack.yml` installs 16 roots covering .NET configuration, coding guidance,
an MIT license, ignore rules, GitHub pull-request automation, Copilot setup,
and staged-file quality.

Two current roots are also dependencies of `dotnet-project`:
`dotnet-build-config` and `dotnet-central-package-management`. Luna resolves each
shared node once, so this is not duplicate file application. The direct roots
do, however, express independent ownership. Remove them as direct roots only if
uninstalling `dotnet-project` should also remove both policies.

Existing packs demonstrate most important Luna capabilities:

- `dotnet-project` and `github-commitlint-workflow` use exact-version
  composition.
- `license-mit` uses a defaulted parameter and Scriban template.
- Gitignore packs use section merges, while SDK and tool manifests use JSON
  merges.
- `commitlint` and `csharpier` use script hooks.
- `github-pull-request-gate-workflow` uses a non-executable instruction hook.

## Implementation status

Implemented atomic packs include `markdownlint-config`,
`prettier-config`, `github-community-health`, `github-issue-forms`,
`repository-contribution-guide`, both GitHub Copilot instruction packs,
`github-copilot-setup-workflow`, split EditorConfig packs, `vscode-dotnet-workspace`,
`husky`, and target-specific lint-staged contributors.

Implemented composites include `dotnet-repository`, `dotnet-coding-guidance`,
`github-pull-request-quality`, `repository-documentation-quality`,
`github-open-source-baseline`, `lint-staged-quality`,
`lint-staged-dotnet-quality`, `husky-lint-staged`, and
`husky-lint-staged-dotnet-quality`.

The .NET build composite action remains intentionally unpackaged. Shared
MSBuild XML also remains whole-file policy because LunaPack has no structural
XML ownership strategy.

## Selection principles

A repository asset is a strong pack candidate when it represents a reusable
policy or complete tool capability, has a stable target, and can be updated
without knowing Luna's source layout. Prefer atomic packs that remain useful on
their own, then provide composites for opinionated adoption paths.

Do not pack an asset merely because another repository could copy it. A pack
should own content over time, have understandable compatibility boundaries,
and leave consumers with a coherent result after install, update, and
uninstall.

## Existing packs

### Keep as focused building blocks

The following packs already have useful, portable boundaries:

| Pack                                | Assessment                                                                                                                     |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| `gitignore-baseline`                | Good language-neutral section merge.                                                                                           |
| `dotnet-gitignore`                  | Good .NET-specific companion to general ignores.                                                                               |
| `csharpier`                         | Good JSON merge and tool-restore hook example.                                                                                 |
| `license-mit`                       | Good parameterized template with explicit overwrite behavior.                                                                  |
| `madr-template`                     | Portable template, but its default target should align with any future ADR guidance pack.                                      |
| `clean-code-guidelines`             | Portable language-neutral guidance.                                                                                            |
| `csharp-guidelines`                 | Portable C# guidance; a future composite can pair it with clean-code guidance.                                                 |
| `github-pull-request-gate-workflow` | Reusable when consumers need to aggregate external checks. Its required-check setup is correctly an instruction, not a script. |

Keep `dotnet-sdk-10` explicit rather than replacing it with a parameterized
`dotnet-sdk`. SDK major and test-runner choices are compatibility policy, not
consumer styling. A future .NET 11 pack can coexist with the tested .NET 10
pack and make migration intent clear.

### Refine before broader sharing

Some current packs are useful but more opinionated than their names suggest:

| Pack                                | Refinement                                                                                                                                                                                                                                 |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `dotnet-editorconfig`               | Split language-neutral editor rules from C# rules, or rename it to state that it also owns Markdown, YAML, shell, batch, Makefile, and TypeScript conventions.                                                                             |
| `dotnet-build-config`               | Separate baseline build policy from analyzer and TUnit package references if consumers need independent choices. It currently fixes `net10.0`, latest C#, CSharpier, Meziantou.Analyzer, TUnit, and filesystem test helpers as one policy. |
| `dotnet-central-package-management` | Keep aligned with the package references selected by build-policy packs; alone it declares versions that a consumer may never use.                                                                                                         |
| `dotnet-project`                    | Keep its current narrow meaning, or introduce a new broader composite instead of silently adding unrelated repository tooling.                                                                                                             |
| `commitlint`                        | Remove LunaPack wording while keeping optional allowed scopes configurable.                                                                                                                                                                |
| `github-commitlint-workflow`        | Describe it as PR-title validation. Current workflow does not validate linked Azure Boards work items despite its pack description. Parameterize the target branch and Node version only where consumers genuinely vary them.              |

## Atomic pack candidates

### First candidates

These assets are both present in this repository and broadly reusable.

| Proposed pack                               | Repository evidence                                                                                                | Recommended Luna features                                                                                                                                 | Boundary                                                                                                                                                 |
| ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `markdownlint-config`                       | `.markdownlint.json` disables only line-length enforcement.                                                        | Copy the static configuration; optionally add an instruction explaining supported CLI/editor integration.                                                 | Configuration only. Do not force Node tooling on consumers that use another Markdownlint host.                                                           |
| `prettier-config`                           | `prettier.config.cjs` extends `prettier-config-standard` and specializes YAML.                                     | Copy the configuration. Use a script hook only in an explicitly tool-installing variant; otherwise instruct consumers to install exact peer dependencies. | Node-based formatting policy, independent of Luna's package scripts.                                                                                     |
| `github-community-health`                   | `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`, and issue forms provide a coherent public-maintenance baseline. | Use a directory selector, Scriban parameters, conditions, and path-aware links.                                                                           | Parameterize project name, conduct/security contact, repository URL, support channel, and supported-version statement. Remove Luna trust-model text.     |
| `github-copilot-documentation-instructions` | Markdown, changelog, documentation, and ADR scoped instructions already encode reusable authoring policy.          | Manage `.github/instructions` files; use Scriban and `files.path` so links follow remapped companion guidance.                                            | Exclude Luna audience paths unless bundled with the matching documentation structure.                                                                    |
| `github-action-dotnet-build`                | `.github/actions/build-dotnet/action.yml` is already a parameterized composite action.                             | Copy the action directory and provide an instruction for the calling workflow.                                                                            | Deferred: it requires locked restore, `global.json`, and Microsoft Testing Platform coverage/report options. It is not a universal `dotnet test` action. |

### Second candidates

These can be valuable after their policy is separated from Luna-specific
values.

| Proposed pack                        | Repository evidence                                                                                             | Required generalization                                                                                                                                                                                     |
| ------------------------------------ | --------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `editorconfig-baseline`              | The root `.editorconfig` contains reusable defaults for Markdown, YAML, shell, batch, Makefile, and TypeScript. | Move C# rules to a separate contributor. Compose contributors using deterministic section or whole-file ownership after testing update behavior.                                                            |
| `vscode-dotnet-workspace`            | `.vscode/settings.json` and `extensions.json` configure formatters and .NET tooling.                            | Parameterize the solution path; remove irrelevant formatter recommendations; use JSON merge so consumer settings survive.                                                                                   |
| `node-staged-quality`                | `package.json` and `.husky/pre-commit` combine Husky, lint-staged, Markdownlint, Prettier, and C# commands.     | Split generic staged-file behavior from Luna paths. Prefer managed `package.json` JSON contributions plus setup instructions; an `npm install` script changes lock files outside Luna ownership.            |
| `github-issue-forms`                 | Bug and feature forms enforce reproducibility, compatibility, and data-safety prompts.                          | Parameterize product name, version command, installation methods, labels, support URL, and security URL. Keep it separate when consumers do not want the full community-health set.                         |
| `github-copilot-dotnet-instructions` | C# and .NET project instruction files point agents to coding and testing guidance.                              | Bundle or depend on matching guidance packs, then generate links with `files.path`. Remove Luna CLI ownership and path assumptions.                                                                         |
| `repository-contribution-guide`      | `CONTRIBUTING.md` has useful setup, validation, PR, documentation, and security structure.                      | Treat it as a template driven by project type, commands, documentation layout, and release policy. A mostly conditional template may be too broad; validate with at least two unrelated repositories first. |

`package.json` should not become one monolithic pack. Its scripts mix CLI,
website, pitch, C#, documentation, and repository setup concerns. Smaller packs
can contribute named scripts and development dependencies through JSON merge,
but ownership and conflicting keys need explicit tests.

## Composite packs

Composite packs are supported today through exact `packs` references. They are
appropriate when the children remain independently useful and the composite
represents a tested adoption profile.

### Recommended composites

| Proposed composite                 | Children in dependency order                                                                                    | Purpose                                                                                                                                                                       |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `dotnet-repository`                | `gitignore-baseline`, `dotnet-gitignore`, `dotnet-sdk-10`, `dotnet-editorconfig`, `csharpier`, `dotnet-project` | Reproduce the repository's core .NET baseline without changing the narrower `dotnet-project` contract.                                                                        |
| `dotnet-coding-guidance`           | `clean-code-guidelines`, `csharp-guidelines`, future `github-copilot-dotnet-instructions`                       | Install human guidance and matching agent instructions together. Default targets must be aligned before composition because a composite cannot supply a consumer destination. |
| `github-pull-request-quality`      | refined `github-commitlint-workflow`, `github-pull-request-gate-workflow`                                       | Install title validation and external-check gating as one GitHub profile. Keep each workflow independently installable.                                                       |
| `repository-documentation-quality` | `markdownlint-config`, `prettier-config`, future `github-copilot-documentation-instructions`, `madr-template`   | Align formatting, linting, ADR authoring, and agent guidance. Keep Node dependency installation opt-in.                                                                       |
| `github-open-source-baseline`      | `github-community-health`, refined `github-issue-forms`, optional contribution-guide pack                       | Provide public repository governance without application build policy. Avoid duplicate ownership if issue forms remain inside the community-health pack.                      |

Do not use one Boolean parameter to make every child optional. Conditions select
managed files; they do not conditionally remove dependency edges. Publish
smaller composites for materially different profiles.

### Composition constraints

- Dependencies use exact versions. Release a new composite version whenever a
  child version changes.
- Dependency order controls managed-file merge contributions and lifecycle
  hook order. Test the declared order rather than treating it as metadata.
- Parameters share one graph-wide namespace. Prefix generic names where
  collision is plausible, such as `communitySecurityContact` rather than
  `email`.
- A composite binding is graph-wide, not private to one child. Avoid two child
  references trying to bind the same hidden parameter differently.
- Directly installed roots keep their hooks enabled. A composite can suppress
  selected dependency events with `disabledHooks`, useful when a profile wants
  configuration but not each child's setup script.
- Consumer remapping can change effective targets, but composites do not carry
  project-level destination choices. Atomic defaults must compose sensibly.

## Luna feature usage

Use Luna features according to ownership and risk:

| Feature                               | Good use in proposed packs                                                                                                                          | Avoid                                                                                                         |
| ------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Source, directory, and glob selectors | Copy issue-form or scoped-instruction directories while preserving relative layout.                                                                 | Broad globs that accidentally publish Luna-only files.                                                        |
| Copy strategies                       | Own generated policy files; use `fail-if-exists` for content requiring deliberate adoption and overwrite only for explicitly replaceable templates. | Overwriting existing community or contribution policy without clear consent.                                  |
| Line, section, and JSON merge         | Add ignore sections, tool manifests, VS Code settings, and bounded `package.json` contributions.                                                    | Assuming XML, JavaScript, or YAML can be structurally merged; use full ownership or separate files there.     |
| Typed parameters and conditions       | Configure contacts, repository URLs, branch names, optional issue forms, and enum/multi-select policy choices.                                      | Parameterizing tested compatibility facts such as SDK major or action commit pins.                            |
| Scriban templates                     | Render public names, contacts, URLs, allowed Commitlint scopes, and conditional text. Use `files.path` for links between managed documents.         | Enabling templates on static or binary content; commands, runners, and script file paths cannot be templated. |
| Script hooks                          | Restore a local tool when configuration alone is unusable and the side effect is narrow, reviewable, and idempotent.                                | Routine package-manager mutation, broad bootstrap scripts, secrets, or repository-specific release actions.   |
| Instruction hooks                     | Explain required GitHub settings, dependency installation, moderation contacts, or manual activation after files are installed.                     | Using executable scripts for account or repository settings that require human review.                        |
| Composite packs                       | Publish tested, opinionated profiles from reusable atomic packs.                                                                                    | Hiding unrelated policy in a broad mandatory baseline.                                                        |

## Keep Luna-specific

The following assets should not become shared packs in their current form:

- `build.ps1`, `test.ps1`, CLI workflows, and CLI build/release actions encode
  Luna project paths, suites, Native AOT runtimes, artifacts, and publication.
- Website workflows and actions encode the Luna documentation site and its
  deployment model.
- Pack/schema instruction files, lifecycle safety guidance, path-handling
  policy, and most ADRs govern LunaPack implementation itself.
- `copilot-setup-steps.yml`, RTK hooks, agent definitions, prompts, and skills
  reflect this repository's agent environment rather than a general project
  contract.
- `CODEOWNERS` and the pull-request template encode current team ownership and
  review policy. They need separate productization rather than direct copying.
- Luna CLI, schema, website, pitch, release, and generated test-result content
  are products or outputs, not reusable repository policy.

## Remaining sequence

1. Preserve each published pack release in an immutable version directory.
2. Validate portable candidates in at least one unrelated repository before
   publishing them for others.
3. Add fixture-driven lifecycle tests for every composite and Boolean branch.
4. Revisit the .NET build action only after its Luna-specific prerequisites can
   be represented without claiming a generic contract.
5. Prototype structural XML and YAML ownership before splitting shared build
   and workflow files further.

For each candidate, test Windows-style input where paths are accepted, exact
persisted targets, existing-file conflict behavior, repeated install and
update, remapped targets, script-skip behavior, and uninstall ownership. For
composites, also test dependency ordering, shared-node deduplication, parameter
collisions, disabled hooks, and removal of a child between versions.
