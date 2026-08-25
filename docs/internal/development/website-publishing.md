# Publish The Developer Website

The developer website presents the canonical public guidance in
`docs/developer`. Do not copy developer Markdown into
`projects/website/src`; that project owns Docusaurus configuration, assets, and
presentation only.

`sidebars.js` owns the developer-documentation journey. Keep its explicit
order: what LunaPack is, getting started, CLI reference, then the Diataxis pack
guide. Add or move a developer page in that sidebar as part of the same change.

## Build Locally

Use the website's isolated npm project:

```powershell
npm ci --prefix projects/website
npm run build --prefix projects/website
npm run serve --prefix projects/website
```

`npm run serve` publishes the generated `build` directory locally. The site
uses the production `/lunapack/` base path, so verify the landing
page, developer-doc navigation, and linked CLI and pack pages from that path.

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
`projects/website/src/**`, the website composite action, or either website
workflow. It never deploys.

`website.yml` uses the same composite action and path filters for pushes to
`main`, then deploys only `projects/website/src/build` to the `github-pages`
environment. Before the first deployment, configure the repository's Pages
source as **GitHub Actions** in GitHub repository settings. The initial site
address is:

```text
https://lunapack.dev/
```
