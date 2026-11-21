# Design

## Context

See `proposal.md` - Why. The existing Docusaurus setup already consumes
`docs/developer` as an external docs path, while the canonical CLI changelog
is owned by `projects/cli/CHANGELOG.md`. ADR-0067 defines that file as the
single CLI release-history source, and ADR-0024 defines the website as a
Docusaurus build over repository-owned Markdown.

The website currently rebuilds only for `docs/developer/**`,
`projects/website/src/**`, and workflow changes. A changelog-only change would
therefore leave the deployed page stale unless that path is added to both
website workflows.

## Goals / Non-Goals

**Goals:**

- Render the canonical CLI changelog as `/changelog` without a committed
  duplicate.
- Provide a direct main-navigation link and consistent Docusaurus styling.
- Hide the maintainer-only `Unreleased` placeholder.
- Rebuild validation and production sites when the canonical changelog changes.
- Preserve the existing npm and Docusaurus dependency model.

**Non-Goals:**

- Move or split the canonical changelog out of `projects/cli/CHANGELOG.md`.
- Add changelog search, filtering, or a custom interactive release timeline.
- Change CLI release-note extraction or package changelog contents.
- Copy public changelog content into `docs/developer`.

## Decisions

### Generate a transient Docusaurus document from the canonical changelog

The website build will run a small Node preparation script before
`docusaurus start` and `docusaurus build`. The script reads
`projects/cli/CHANGELOG.md` and writes a transient `index.md` under the website
source's generated area.

The generated document will add only presentation metadata and remove the
`Unreleased` section when it contains no real release notes. Docusaurus will
consume this generated document through a second docs-plugin instance.

Options considered:

- **Direct docs plugin over `projects/cli/CHANGELOG.md`:** simpler, but creates
  a route tied to the source filename and exposes the maintainer placeholder.
- **Committed copy under `docs/developer`:** conventional for Docusaurus, but
  violates ADR-0067's one-source-of-truth ownership and creates drift.
- **Custom React/parser page:** polished, but unnecessary for initial use and
  introduces more presentation code and dependencies.

Chosen option: build-time generated document, because it keeps
`projects/cli/CHANGELOG.md` canonical while giving Docusaurus a clean,
predictable document route.

### Use a dedicated docs-plugin instance for the changelog route

The Docusaurus config will add a second docs-plugin instance with a distinct
`changelog` ID, generated-document path, `changelog` route base, and no
sidebar. The generated `index.md` will provide the `/changelog/` route.

The main navbar will include a left-positioned `Changelog` item. Optional
custom CSS will improve release and category heading hierarchy without
changing the canonical Markdown.

### Filter only the maintainer placeholder

The preparation script will parse Markdown heading boundaries. It will remove
the `Unreleased` section only when it has no content other than the existing
placeholder text or whitespace. A real list entry or future changelog text
under `Unreleased` remains visible.

The script will not rewrite released version headings or bullet content.

### Add the canonical changelog to website workflow triggers

Both website workflows will include `projects/cli/CHANGELOG.md` in their
pull-request and push path filters. No release workflow or CLI package workflow
will consume the generated website document.

The existing shared website build action will continue to run
`npm ci` and the existing build command. The website package scripts will
invoke changelog preparation before local start and production build.

### Record the source boundary internally

The website publishing guide will document the build-time input and generated
file boundary. Because this decision extends website source ownership beyond
`docs/developer`, implementation should add a sequential architecture decision
record (next number: ADR-0106) rather than modifying ADR-0024 or ADR-0067.
The new record should reference both existing decisions.

## Risks / Trade-offs

- **Generated content can become stale locally:** pre-start/pre-build scripts
  regenerate it every time and the generated directory remains outside the
  committed source set.
- **Placeholder filtering can become brittle:** isolate filtering behind the
  heading parser and cover the exact existing placeholder in a focused test.
- **Second docs-plugin routing can differ between start and build:** validate
  `/changelog/` in the local production build and in the route metadata.
- **Markdown rendering has fewer visual features than a custom component:** the
  initial change prioritizes source reuse and predictable rendering; richer
  cards, filters, or search remain future scope.

## Migration Plan

No migration or CLI behavior change is required. Deployment consists of adding
the generated document preparation, Docusaurus route, navigation item, workflow
trigger, and documentation.

Rollback removes the route/plugin, navigation item, workflow path, and
preparation script entry. It does not modify or remove any canonical CLI
changelog content.
