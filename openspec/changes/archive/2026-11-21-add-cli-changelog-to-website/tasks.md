# Tasks

## 1. Generate the changelog document

- [x] 1.1 Add a Node preparation script that reads
  `projects/cli/CHANGELOG.md`, removes only the placeholder-only `Unreleased`
  section, and writes a generated `index.md` for Docusaurus. Verify it by running
  the script and inspecting the generated route source.
- [x] 1.2 Add focused preparation-script tests for the placeholder-only
  `Unreleased` section, real unreleased entries, and released version sections.
  Verify with `node --test`.
- [x] 1.3 Add npm scripts that prepare the changelog before local start and
  production build, and exclude generated output from source control. Verify
  `npm ci --prefix projects/website` and both npm entry points succeed.

## 2. Integrate the changelog page

- [x] 2.1 Add a dedicated Docusaurus docs-plugin instance for the generated
  changelog and a left-positioned `Changelog` navbar item. Verify the
  production build creates `/changelog/` and the navbar reaches it.
- [x] 2.2 Add concise page-specific CSS for release and category headings,
  preserving the existing theme and responsive layout. Verify the production
  build and local browser rendering.
- [x] 2.3 Update `docs/internal/development/website-publishing.md` and create
  accepted ADR-0106 for the canonical CLI changelog source boundary. Verify
  Markdown links and update the ADR index.

## 3. Update website delivery triggers

- [x] 3.1 Add `projects/cli/CHANGELOG.md` to the pull-request website workflow
  path filters. Verify a changelog-only PR change selects the build workflow.
- [x] 3.2 Add `projects/cli/CHANGELOG.md` to the main-branch website workflow
  path filters. Verify a changelog-only main-branch push selects the build and
  publish workflow.

## 4. Integration verification

- [x] 4.1 Run the website install and production build, then serve the static
  output and verify `/changelog/` renders released sections, hides the
  maintainer placeholder, and resolves navigation and assets.
- [x] 4.2 Validate the OpenSpec change and Markdown with the repository's
  documentation and strict OpenSpec checks. Verify no broken links or strict
  OpenSpec warnings remain.
