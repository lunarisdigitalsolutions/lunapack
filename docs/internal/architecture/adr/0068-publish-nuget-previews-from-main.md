---
status: accepted
date: 2026-08-31
decision-makers: LunaPack maintainers
---

# ADR-0068: Publish And Dispatch CLI Releases

## Context and Problem Statement

Tagged Luna releases publish GitHub, npm, NuGet, and container distributions.
CLI consumers also need installable previews of consumer-visible changes on
`main`, without creating releases or publishing those changes through every
distribution channel. Preview consumers need the pending changelog entries
rather than history for earlier stable versions.

Stable and preview publication originally shared one composite action that also
implemented every publication channel directly. That mixed release intent,
shared artifact preparation, and registry-specific behavior. Channel changes
could affect unrelated distributions, and specialized NuGet artifacts had no
clear owning publisher.

## Decision Drivers

- Publish previews only when a commit to `main` changes `projects/cli`.
- Preserve MinVer and Semantic Versioning ordering after the latest stable tag.
- Keep the RID-specific .NET tool packages and pointer package consistent.
- Exclude npm, containers, GitHub Releases, and GitHub Actions artifacts.
- Package only pending CLI release notes in preview packages.
- Keep stable and preview release policy explicit in one entry point.
- Isolate registry-specific authentication and publication behavior.
- Prevent container and npm publication from depending on GitHub publication.
- Prepare common archive inputs once while keeping specialized artifacts in
  their consuming channel.

## Considered Options

- Publish previews through a separate workflow.
- Publish only a NuGet pointer package from `main`.
- Keep one composite action for all release behavior.
- Call channel actions directly from the workflow.
- Add a branch-only preview path and dispatch selected publication channels.

## Decision Outcome

Chosen option: "Add a branch-only preview path and dispatch selected
publication channels," because stable and preview publication share one
workflow identity and release policy while registry behavior remains isolated.

The reusable `release` composite accepts `release-type: stable` or
`release-type: preview`. Stable remains the default and selects
`github,container,npm,nuget`; preview selects only `nuget`. The action performs
shared stable preparation, then delegates the selected list to the
`release/channels` dispatcher. The dispatcher rejects unknown or duplicate
channel names and calls one composite publisher per channel in a fixed order.

For stable releases, shared preparation downloads and validates the five CLI
archives, extracts release notes, writes checksums, and stages native and npm
package trees. GitHub, container, and npm consume that shared state
independently; none depends on another channel running first. The NuGet channel
owns downloading and validating the five RID-specific packages, building the
pointer package, authenticating, and publishing RID packages before the pointer
package.

Stable releases run from Git tags such as `v1.2.0`. Preview publication runs
from untagged `main` commits that change `projects/cli`, using MinVer with the
repository's `v` tag prefix and `preview` identifier. Commits after `v1.2.0`
therefore produce versions such as `1.2.1-preview.1`, which sort after `1.2.0`.
Native runners publish the five RID-specific packages before the pointer
package. Each preview package includes a generated `CHANGELOG.md` containing
only the canonical `Unreleased` section. Stable packages continue to include
the complete CLI changelog. The workflow passes the NuGet.org username from the
`NUGET_USER` GitHub Actions secret. OIDC supplies the temporary publishing
credential; the secret is not a NuGet API key.

### Consequences

- Good, because every qualifying `main` commit produces an installable NuGet
  preview.
- Good, because preview publication cannot create npm, container, release, or
  workflow artifacts.
- Good, because preview package notes describe only pending behavior.
- Good, because stable and preview NuGet trusted publishing uses one workflow
  identity.
- Good, because release intent is separate from current distribution policy.
- Good, because each registry implementation can change without editing other
  publishers.
- Good, because GitHub, container, and npm share archive preparation without
  depending on GitHub Release creation.
- Good, because NuGet staging remains owned by the NuGet publisher.
- Bad, because each qualifying commit consumes five native-runner builds and
  six NuGet package versions.
- Bad, because the combined workflow requires explicit tag and branch job
  conditions.
- Bad, because RID package validation occurs when NuGet publication starts,
  after earlier stable channels may have published.
- Bad, because direct dispatcher calls selecting GitHub, container, or npm must
  provide shared staging; normal workflows call the top-level release action.

### Confirmation

Distribution contract tests verify one workflow name, stable tag and preview
branch conditions, path filters, MinVer command, release-type channel mapping,
channel validation and order, shared preparation before dispatch, native runtime
matrix, RID-before-pointer dependency, channel-owned NuGet staging, Unreleased
changelog override, federated NuGet login, `secrets.NUGET_USER`, dry-run guards,
and absence of other publication channels from preview jobs.

## More Information

This decision supersedes
[ADR-0067](0067-own-and-package-the-cli-changelog.md). The CLI still owns its
canonical changelog; only preview package content differs from stable package
content.
