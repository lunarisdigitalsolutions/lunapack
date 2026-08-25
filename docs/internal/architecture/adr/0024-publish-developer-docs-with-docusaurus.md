---
status: accepted
date: 2026-08-18
decision-makers: LunaPack maintainers
---

# ADR-0024: Publish Developer Documentation With Docusaurus

## Context and Problem Statement

LunaPack developer guidance lives in `docs/developer`, where it is reviewable
alongside the CLI and pack contracts but is not available as a browsable public
site. Copying that content into a website would create competing sources and
make documentation changes harder to review. The repository needs a static
site with a focused pull-request build check and a main-branch publishing path.

The latest stable Docusaurus release selected for this site is `3.10.2`. Its
build-time dependency graph reports 18 high and 6 moderate npm advisories,
including no-fix `image-size` and `serialize-javascript` advisories. No npm
remediation was available on 2026-08-18. Maintainers explicitly approved a
security exception for this bounded dependency graph.

## Decision Drivers

- Keep developer documentation in one canonical, repository-owned location.
- Provide contributors an accurate local and pull-request build signal.
- Publish only static website output with least-privilege GitHub Pages access.
- Make the accepted Docusaurus advisory risk explicit and reviewable.

## Considered Options

- Keep developer documentation available only as repository Markdown.
- Copy documentation into a Docusaurus project.
- Configure Docusaurus to consume `docs/developer` directly.
- Replace Docusaurus because its current stable dependency graph has no-fix
  advisories.

## Decision Outcome

Chosen option: "Configure Docusaurus to consume `docs/developer` directly",
because it provides a useful public documentation experience without creating
a second source of truth.

`projects/website/src` owns the Docusaurus application, package manifest,
exact dependency pins, lock file, assets, and presentation. `docs/developer`
owns the public content. Docusaurus resolves that directory through its
configured external docs path and renders it under the developer-documents
route.

Two workflows enforce this boundary. The pull-request workflow has read-only
permissions and builds the site only. The main-branch workflow rebuilds the
site, uploads only its generated `build` directory, and deploys it through the
GitHub Pages environment.

The approved security exception applies only to the Docusaurus `3.10.2`
dependency graph used to build this static site. It does not authorize new
unresolved high-severity dependencies. Every Docusaurus upgrade and new
high-severity advisory requires a fresh `npm audit --omit=dev` review. The
exception expires when a compatible remediation is available; maintainers must
adopt it or record a replacement decision before the next website release.

### Consequences

- Good, because public content remains editable and reviewable under
  `docs/developer`.
- Good, because pull requests validate the same content source that Pages
  publishes.
- Good, because the Docusaurus advisory risk has a named scope and review
  trigger rather than an implicit exception.
- Bad, because the current website build graph retains no-fix high-severity
  advisories until Docusaurus or its dependencies provide a compatible fix.
- Bad, because the project Pages URL depends on the repository name and base
  path.

### Confirmation

Run `npm ci` and `npm run build` from `projects/website/src`; the build must
render `docs/developer` without copied Markdown content. Confirm that a
developer-documentation pull request runs only the build workflow and a
main-branch change deploys the generated artifact to GitHub Pages. Review
`npm audit --omit=dev` on every Docusaurus upgrade and when npm reports a new
high-severity advisory.

## More Information

See [website publishing guidance](../../development/website-publishing.md) for
operational commands and the GitHub Pages prerequisite.
