---
status: accepted
date: 2026-11-21
decision-makers:
  - Lunaris Engineering
---

# ADR-0106: Render the CLI Changelog from Its Canonical Source

## Context and Problem Statement

ADR-0067 gives `projects/cli/CHANGELOG.md` canonical ownership of Luna CLI
release history. ADR-0024 defines Docusaurus and GitHub Pages as the website
delivery mechanism for repository-owned public documentation.

The website needs a polished top-level changelog route, but copying
`projects/cli/CHANGELOG.md` under `docs/developer` or into the website source
would create a second maintained copy and allow CLI and website release notes
to drift.

## Decision Drivers

- Keep CLI release history owned and reviewed in one canonical file.
- Render that history as public website content without duplicated Markdown.
- Produce a stable `/changelog/` route with the existing Docusaurus theme.
- Keep website builds reproducible and independent of new runtime services.
- Trigger validation and publication for canonical changelog changes.

## Considered Options

- Add a Docusaurus docs plugin that reads `projects/cli/CHANGELOG.md` directly.
- Commit a website-maintained copy under `docs/developer`.
- Generate a transient Docusaurus document before website start and build.
- Build a custom React changelog parser and presentation component.

## Decision Outcome

Chosen option: "Generate a transient Docusaurus document before website start
and build," because it preserves canonical CLI ownership without Docusaurus
filename routing or maintainer placeholder concerns in the source changelog.

The website preparation script reads `projects/cli/CHANGELOG.md`, removes a
placeholder-only `Unreleased` section, and writes a generated `index.md`
inside `projects/website/src/.generated/changelog`. A dedicated Docusaurus
docs-plugin instance renders that generated route under `/changelog/`.

The canonical file remains unchanged by website presentation requirements.
The generated directory remains outside source control and is regenerated for
both local start and production build.

### Consequences

- Good, because CLI release notes have one maintained source.
- Good, because the website build has no additional runtime dependency or
  external service.
- Good, because the public route uses the existing Docusaurus theme and search
  integration.
- Bad, because a generated document adds a small build preparation step.
- Bad, because changelog formatting or placeholder behavior depends on the
  preparation script remaining aligned with the canonical Markdown structure.

### Confirmation

Focused Node tests cover placeholder-only `Unreleased` content, real
`Unreleased` entries, and released version sections. The production website
build must generate `/changelog/`, hide the maintainer placeholder, show
release headings and notes, and expose the navbar link.

Pull-request and main-branch website workflows include
`projects/cli/CHANGELOG.md` in their path filters.

## More Information

This decision complements
[ADR-0024](0024-publish-developer-docs-with-docusaurus.md) and
[ADR-0067](0067-own-and-package-the-cli-changelog.md). It does not change CLI
release-note extraction or package changelog contents.
