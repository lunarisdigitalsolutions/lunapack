## Context

See [proposal.md](proposal.md) for the motivation. The repository has
developer-facing Markdown beneath `docs/developer` and no website project or
Pages workflow. Its existing GitHub Actions workflows pin third-party actions
to commit SHAs. Root `package.json` serves repository tooling, whereas this
website must own all of its npm metadata beneath `projects/website/src`.

The repository owner and name establish the initial project Pages address:
`https://lunapack.dev/`. This change
does not introduce a custom domain.

## Goals / Non-Goals

**Goals:**

- Publish the existing developer-documentation source through a responsive,
  work-focused Docusaurus site.
- Make local and CI builds reproducible without coupling the site to root npm
  dependencies.
- Use the same source-path contract for pull-request validation and
  main-branch publication.
- Record the durable documentation-source and publishing boundary for
  maintainers.

**Non-Goals:**

- Change LunaPack CLI behavior, schemas, pack lifecycle, or existing developer
  documentation claims.
- Publish `docs/internal`, `docs/product`, or pitch material.
- Introduce a custom domain, CMS, search service, analytics, or interactive
  application backend.
- Establish a broad website release-versioning process.

## Decisions

### Keep the Docusaurus project self-contained under the requested source root

The Docusaurus project root will be `projects/website/src`. It will contain
`package.json`, `package-lock.json`, Docusaurus configuration, sidebar
configuration, static assets, and the site source directory. Its scripts will
provide local development, build, and static-preview commands. The root
`package.json` will not gain website dependencies or scripts.

At implementation time, direct package versions will be resolved from the npm
registry to the latest compatible stable releases, written without range
prefixes, and locked with npm. The project will include Docusaurus, React,
React DOM, locally packaged display/body fonts, and Lucide React icons only as
needed by the finished site. CI will install through `npm ci` from this local
lock file.

Alternative considered: add Docusaurus to the repository root package. Rejected
because it combines independent toolchains and violates the requested local
dependency boundary.

### Treat `docs/developer` as the website's external content source

The default Docusaurus documentation plugin will resolve its content path from
the website root to `../../../docs/developer`, mount it below the developer
documentation route, and derive sidebar navigation from the existing folders.
The existing Markdown files stay in place; the website contributes
presentation, navigation, and landing-page links only. Relative links inside
the developer documentation remain authored as repository documentation and
are validated by the static build.

Alternative considered: copy the documentation beneath the Docusaurus project.
Rejected because copies drift and make pull-request review ambiguous. A
filesystem symlink was also rejected because it is less portable across Windows
and GitHub-hosted Linux environments than an explicit configured content path.

### Build a restrained developer landing page around real workflow evidence

The homepage will use a clear editorial hierarchy: a first-viewport LunaPack
product signal and outcome, the current standardization problem, the pack-based
solution, an end-to-end workflow, a supported CLI example, benefits, and a
documentation call to action. Copy will use the current CLI documentation as
its source of truth and state limitations rather than implying unimplemented
functionality.

The visual system will use purposeful local fonts, a light neutral base with
contrasting green and red-orange accents, compact panels, Lucide icons, and an
original static bitmap that depicts a versioned-pack workflow. It will include
responsive layout constraints and keyboard-accessible links and navigation.
This keeps the site inspectable and product-specific without a generic
marketing layout.

Alternative considered: use the Docusaurus starter homepage unmodified.
Rejected because it does not explain the product or provide the requested
onboarding path.

### Separate build validation from GitHub Pages publication

Two workflow files will share path filters for `docs/developer/**`,
`projects/website/src/**`, and their own workflow files:

- `website-pr.yml` will run on pull requests targeting `main`, install locked
  dependencies, and execute the Docusaurus build. It has `contents: read`
  permission only and no deployment steps.
- `website.yml` will run on pushes to `main` and manually. It will use the
  same install/build sequence, upload only `projects/website/src/build`, and
  deploy through the GitHub Pages action. It will grant only `contents: read`,
  `pages: write`, and `id-token: write`; its concurrency group will preserve an
  in-progress deployment.

Both workflows will pin third-party Actions to reviewed commit SHAs, following
the existing CI convention. The deploy workflow will configure Docusaurus with
the repository Pages URL and `/lunapack/` base path so generated
asset and route URLs work after publication.

Alternative considered: use one workflow for pull requests and deployment.
Rejected because untrusted pull requests must not receive Pages publication
permissions or side effects.

### Document the durable publishing boundary

Implementation will add ADR-0024 to record that developer documentation stays
at `docs/developer`, the website consumes it through Docusaurus configuration,
and GitHub Actions publishes the static output. An internal maintainer guide
will explain local verification, dependency updates, source-path triggers, and
the one-time GitHub Pages repository setting. The ADR index and Unreleased
changelog will be updated with the implementation.

Alternative considered: document only the npm commands in the README. Rejected
because source ownership and CI publishing are architecture and maintainer
workflow decisions that need durable internal documentation.

## Risks / Trade-offs

- [Existing Markdown or relative links are incompatible with Docusaurus] ->
  Build the site locally and in the pull-request workflow; repair only the
  affected developer-documentation source link or front matter.
- [A Pages project-site base path breaks routes or asset URLs] -> Configure the
  known repository base path and verify the deployed environment URL after the
  first main-branch deployment.
- [A current stable npm package upgrade changes Docusaurus behavior] -> Pin
  every direct version, commit the lock file, and require a clean local/CI
  build before merging.
- [GitHub Pages is not configured to use GitHub Actions] -> Enable that source
  once in repository settings before the first deployment; the maintainer guide
  will name this prerequisite.
- [External documentation-source ownership becomes unclear] -> ADR-0024 and
  the internal guide explicitly assign content ownership to `docs/developer`
  and presentation ownership to `projects/website/src`.

## Migration Plan

1. Create and build the website package locally with the locked dependency
   graph; correct Docusaurus compatibility issues in the affected source.
2. Add the PR workflow and confirm a documentation-site change runs the
   build-only check.
3. Add the main deployment workflow, then configure repository Pages to deploy
   from GitHub Actions if it is not already configured.
4. Merge to `main` and confirm the published environment URL renders the
   landing page, assets, navigation, and developer documentation.
5. To roll back, revert the website/deployment change or disable the Pages
   workflow; canonical documentation remains intact under `docs/developer`.
