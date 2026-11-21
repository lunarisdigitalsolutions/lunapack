# Proposal

## Why

CLI users need a polished, discoverable view of Luna CLI releases from the main
LunaPack website. The canonical release history already lives in
`projects/cli/CHANGELOG.md`, but copying it into the website would create a second
source of truth and allow website and packaged release notes to drift.

## What Changes

- Add a top-level `/changelog` page to the Docusaurus website.
- Add a `Changelog` entry to the main website navigation.
- Render the canonical `projects/cli/CHANGELOG.md` directly during the website
  build instead of maintaining a duplicate Markdown file.
- Present the changelog with Docusaurus Markdown rendering and focused
  page styling. Hide the maintainer-only `Unreleased` placeholder until it
  contains real release notes.
- Rebuild pull-request and production website builds when the canonical
  changelog changes.
- Keep the initial page format Markdown-based; no custom timeline, search, or
  release filtering is added in this change.

## Capabilities

### New Capabilities

- `cli-changelog-presentation`: Publish the canonical Luna CLI changelog as a
  polished top-level website page with stable navigation and release-history
  formatting.

### Modified Capabilities

- `developer-documentation-delivery`: Add `projects/cli/CHANGELOG.md` to the
  pull-request validation and GitHub Pages publication path filters.

## Impact

- Website configuration and presentation: `projects/website/src`.
- Canonical content source: `projects/cli/CHANGELOG.md`.
- Delivery workflows: `.github/workflows/website-pr.yml` and
  `.github/workflows/website.yml`.
- Product documentation: no implementation contract or feature status changes.
- Developer documentation: no copied changelog page under `docs/developer`;
  public navigation and release presentation change only.
- Internal documentation: update the website publishing guide; add or update
  an accepted architecture record only if the source-boundary decision requires
  durable architectural documentation.
