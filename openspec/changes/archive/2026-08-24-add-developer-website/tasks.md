## 1. Create the isolated Docusaurus project

- [x] 1.1 Create the Docusaurus project root at `projects/website/src` with
      local npm scripts for development, production build, and static preview;
      do not modify root npm dependencies.
- [x] 1.2 Query npm for the latest compatible stable releases, add only the
      required Docusaurus, React, font, and icon packages with exact version pins,
      and commit `projects/website/src/package-lock.json`.
- [x] 1.3 Configure the site name, GitHub project Pages URL and
      `/lunapack/` base path, static asset output, and navigation.
- [x] 1.4 Configure Docusaurus to load `../../../docs/developer` directly,
      derive CLI and pack navigation from that hierarchy, and confirm no developer
      Markdown files are copied into the website project.

## 2. Build the developer experience

- [x] 2.1 Implement the responsive landing page with a prominent LunaPack product
      signal, outcome-led hero, problem, solution, workflow, supported CLI example,
      benefits, and documentation call to action.
- [x] 2.2 Add the visual system, local font loading, original static workflow
      bitmap, Lucide icons, responsive layout constraints, and accessible focus,
      navigation, and link states.
- [x] 2.3 Verify landing-page commands and claims against
      `docs/developer/cli`; correct only Markdown/MDX compatibility issues found
      while rendering the canonical `docs/developer` source.
- [x] 2.4 Refine the landing page for technical leads with the Lunaris palette,
      accessible terminal contrast, mobile layouts, supported pack authoring
      examples, and a curated developer-documentation journey.

## 3. Automate validation and publication

- [x] 3.1 Add `.github/workflows/website-pr.yml` for build-only pull-request
      validation against `main`, using shared documentation-site path filters,
      `npm ci`, and `npm run build` from `projects/website/src` with read-only
      permissions.
- [x] 3.2 Add `.github/workflows/website.yml` for `main` push and manual
      deployment, using the same path filters, locked install/build, the generated
      `build` directory as the only Pages artifact, least-privilege Pages
      permissions, and non-cancelling deployment concurrency.
- [x] 3.3 Pin every third-party GitHub Action in the new workflows to reviewed
      commit SHAs consistent with existing repository workflows.

## 4. Record the publishing boundary

- [x] 4.1 Create accepted ADR-0024 from the MADR template documenting the
      canonical `docs/developer` source, Docusaurus presentation boundary, and
      GitHub Pages delivery model; add it to the ADR index.
- [x] 4.2 Add an internal maintainer guide for local website commands,
      dependency update policy, CI path filters, deployed URL, and the GitHub
      Pages-from-Actions repository prerequisite; link it from the internal
      development index.
- [x] 4.3 Add an Unreleased `CHANGELOG.md` entry for the developer
      documentation website and GitHub Pages publication.

## 5. Validate the finished change

- [x] 5.1 From `projects/website/src`, run a clean `npm ci` followed by the
      production build and confirm the build renders the external developer
      documentation source.
- [x] 5.2 Run the local static preview and inspect desktop and mobile layouts,
      documentation navigation, CLI example, links, and base-path asset URLs.
- [x] 5.3 Run the repository Markdown and structured-file formatting/lint
      checks for changed documentation and workflow files, then validate the
      OpenSpec change in strict mode.
- [x] 5.4 After merge, verify the GitHub Pages deployment URL and confirm a
      documentation-only pull request receives the build-only workflow check.
