## Why

LunaPack developer guidance is Markdown in the repository, so prospective users
cannot browse it as a discoverable documentation site and contributors cannot
validate the published presentation before merge. A static developer website
and focused Pages automation make the current CLI and pack guidance easier to
find, navigate, and release without duplicating its source content.

## What Changes

- Add a Docusaurus website rooted at `projects/website/src` with a
  developer-focused landing page that explains the current LunaPack pack workflow
  through concise outcomes, examples, and links into the documentation.
- Configure Docusaurus to consume `docs/developer` as its documentation source
  without copying or relocating the Markdown files; generate navigation from
  that existing hierarchy.
- Keep website dependencies and npm metadata within `projects/website/src`.
  Pin every direct dependency to an exact current stable version and commit the
  npm lock file so CI installs the resolved dependency graph reproducibly.
- Add a GitHub Pages workflow that builds the website and deploys its static
  output after pushes to `main` that affect developer docs or website sources.
- Add a pull-request workflow with the same relevant path filters that installs
  and builds the website only.
- Record the documentation-source and publishing boundary in an ADR, add its
  index entry, document the maintainer workflow, and add an Unreleased
  changelog entry.

## Capabilities

### New Capabilities

- `developer-documentation-site`: Provide a responsive Docusaurus developer
  site and landing page while treating `docs/developer` as the canonical
  documentation source.
- `developer-documentation-delivery`: Validate documentation-site builds on
  pull requests and publish main-branch builds to GitHub Pages.

### Modified Capabilities

- None.

## Impact

- Affected website source: new standalone npm package, Docusaurus
  configuration, site assets, landing-page components, styles, and navigation
  under `projects/website/src`.
- Affected automation: new GitHub Actions workflows under `.github/workflows`
  with Pages permissions, path filters for `docs/developer/**` and
  `projects/website/src/**`, and a build-only pull-request check.
- Affected developer documentation: `docs/developer` remains the public source
  material, now rendered by the website rather than duplicated into it.
- Affected internal documentation: ADR-0024, its index, and maintainers'
  website publishing guidance. Product documentation has no source-content
  change in this proposal.
- Affected release notes: `CHANGELOG.md` gains an Unreleased entry for the
  published developer documentation site.
