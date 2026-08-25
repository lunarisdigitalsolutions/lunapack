# developer-documentation-delivery Specification

## Purpose

Validate documentation-site changes before merge and publish successful
main-branch builds as the repository's GitHub Pages site.

## Requirements

### Requirement: Validate affected pull requests

The repository SHALL provide a pull-request workflow that runs for pull requests targeting `main` when `docs/developer/**`, `projects/website/src/**`, or the workflow definition changes. The workflow MUST install the locked website dependencies and build the static site. It MUST NOT deploy a Pages artifact or request write-level GitHub Pages permissions.

#### Scenario: Change a developer documentation file in a pull request

- **WHEN** a pull request targeting `main` changes a file under `docs/developer`
- **THEN** the website validation workflow installs dependencies and reports whether the static-site build succeeds

#### Scenario: Change an unrelated file in a pull request

- **WHEN** a pull request targeting `main` changes none of the configured documentation-site paths
- **THEN** the website validation workflow does not run for that change

### Requirement: Publish affected main-branch builds

The repository SHALL provide a GitHub Pages workflow that runs after pushes to `main` when `docs/developer/**`, `projects/website/src/**`, or the workflow definition changes. The workflow MUST install locked dependencies, build the static site, upload only the generated site output, and deploy that artifact to GitHub Pages using the minimum required Pages permissions. Concurrent deployments MUST preserve an in-progress production deployment.

#### Scenario: Merge a developer documentation change

- **WHEN** a change under `docs/developer` is merged into `main`
- **THEN** the Pages workflow builds the site and publishes its generated output to the repository's GitHub Pages environment

#### Scenario: Merge a website source change

- **WHEN** a change under `projects/website/src` is merged into `main`
- **THEN** the Pages workflow builds and deploys the updated static site

### Requirement: Preserve GitHub Pages base-path compatibility

The published website SHALL resolve its static assets and documentation links at the repository's configured GitHub Pages address, including the repository base path for a project Pages site.

#### Scenario: Open the deployed site from GitHub Pages

- **WHEN** a reader opens the Pages deployment URL
- **THEN** the landing page assets, documentation navigation, and linked documentation pages load successfully
