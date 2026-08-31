---
status: accepted
date: 2026-08-31
decision-makers: LunaPack maintainers
---

# ADR-0068: Publish NuGet Previews From Main

## Context and Problem Statement

Tagged Luna releases publish GitHub, npm, NuGet, and container distributions.
CLI consumers also need installable previews of consumer-visible changes on
`main`, without creating releases or publishing those changes through every
distribution channel. Preview consumers need the pending changelog entries
rather than history for earlier stable versions.

## Decision Drivers

- Publish previews only when a commit to `main` changes `projects/cli`.
- Preserve MinVer and Semantic Versioning ordering after the latest stable tag.
- Keep the RID-specific .NET tool packages and pointer package consistent.
- Exclude npm, containers, GitHub Releases, and GitHub Actions artifacts.
- Package only pending CLI release notes in preview packages.

## Considered Options

- Publish previews through a separate workflow.
- Add a branch-only preview path to the existing CLI release workflow.
- Publish only a NuGet pointer package from `main`.

## Decision Outcome

Chosen option: "Add a branch-only preview path to the existing CLI release
workflow," because stable and preview publication then share one workflow
identity while retaining separate triggers, jobs, and distribution scopes.

The reusable release action accepts `release-type: stable` or
`release-type: preview`. Stable remains the default and publishes every
configured distribution. Preview currently publishes only NuGet packages; its
name represents release intent rather than permanently coupling previews to one
registry.

Stable releases run from Git tags such as `v1.2.0`. Preview publication runs
from untagged `main` commits that change `projects/cli`, using MinVer with the
repository's `v` tag prefix and `preview` identifier. Commits after `v1.2.0`
therefore produce versions such as `1.2.1-preview.1`, which sort after `1.2.0`.
Native runners publish the five RID-specific packages before the pointer
package. Each preview package includes a generated `CHANGELOG.md` containing
only the canonical `Unreleased` section. Stable packages continue to include
the complete CLI changelog.

### Consequences

- Good, because every qualifying `main` commit produces an installable NuGet
  preview.
- Good, because preview publication cannot create npm, container, release, or
  workflow artifacts.
- Good, because preview package notes describe only pending behavior.
- Good, because stable and preview NuGet trusted publishing uses one workflow
  identity.
- Good, because stable and preview paths share package setup, validation,
  authentication, and publication behavior in one release action.
- Bad, because each qualifying commit consumes five native-runner builds and
  six NuGet package versions.
- Bad, because the combined workflow requires explicit tag and branch job
  conditions.

### Confirmation

Distribution contract tests verify one workflow name, stable tag and preview
branch conditions, path filters, MinVer command, native runtime matrix,
RID-before-pointer dependency, Unreleased changelog override, federated NuGet
login, and absence of other publication channels from the preview jobs.

## More Information

This decision supersedes
[ADR-0067](0067-own-and-package-the-cli-changelog.md). The CLI still owns its
canonical changelog; only preview package content differs from stable package
content.
