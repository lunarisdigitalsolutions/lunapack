# Publish The Developer Website

The developer website presents the canonical public guidance in
`docs/developer`. Do not copy developer Markdown into
`projects/website/src`; that project owns Docusaurus configuration, assets, and
presentation only.

`sidebars.js` owns the developer-documentation journey. Keep its explicit
order: what LunaPack is, getting started, CLI reference, then the Diataxis pack
guide. Add or move a developer page in that sidebar as part of the same change.

The canonical Luna CLI changelog remains `projects/cli/CHANGELOG.md`. Website
build and start scripts generate a transient Docusaurus document under
`projects/website/src/.generated/changelog`; do not commit or maintain a copy
under `docs/developer`. The generated document removes the maintainer-only
`Unreleased` placeholder before Docusaurus renders `/changelog/`.

## Build Locally

Use the website's isolated npm project:

```powershell
npm ci --prefix projects/website
npm run build --prefix projects/website
npm run serve --prefix projects/website
```

`npm run serve` publishes the generated `build` directory locally. The site
uses the root `/` base path configured for `https://lunapack.dev/`. Verify the
landing page at `/`, developer-documentation routes under `/developer/`, and
linked CLI and pack pages from those paths. Verify the changelog route at
`/changelog/` and confirm it hides the `Unreleased` maintainer placeholder.

## Dependency Updates

Keep every direct dependency in `projects/website/package.json` pinned to
an exact version. Update the package manifest and lock file together, then run:

```powershell
npm ci --prefix projects/website
npm audit --prefix projects/website --omit=dev
npm run build --prefix projects/website
```

Docusaurus `3.10.2` currently has an approved, no-fix build-time dependency
exception: npm audit reports 18 high and 6 moderate advisories. It is limited
to this Docusaurus dependency graph. Reassess it for every Docusaurus upgrade
and new high-severity advisory; adopt a compatible remediation when npm makes
one available. See [ADR-0024](../architecture/adr/0024-publish-developer-docs-with-docusaurus.md).

## GitHub Actions And Pages

`website-pr.yml` runs the `.github/actions/website/build` composite action for
pull requests to `main` that change `docs/developer/**`,
`projects/website/src/**`, `projects/cli/CHANGELOG.md`, the website composite
action, or either website workflow. It never deploys.

`website.yml` uses the same composite action and path filters for pushes to
`main`. Its build job receives content and Pages metadata read permissions and
uploads only `projects/website/src/build`, including hidden paths required for
assets such as `.well-known/security.txt`; a dependent deployment job alone
receives Pages write and OIDC permissions and deploys that artifact to the
`Release` environment. Before the first deployment, configure the repository's
Pages source as **GitHub Actions** and create the `Release` environment in GitHub
repository settings. Configure required reviewers there when website
publication requires maintainer approval. The initial site address is:

```text
https://lunapack.dev/
```
