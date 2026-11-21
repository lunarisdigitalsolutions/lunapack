# Spec Delta

## MODIFIED Requirements

### Requirement: Validate affected pull requests

The repository SHALL provide a pull-request workflow that runs for pull requests targeting `main` when `docs/developer/**`, `projects/website/src/**`, `projects/cli/CHANGELOG.md`, or the workflow definition changes. The workflow MUST install the locked website dependencies and build the static site. It MUST NOT deploy a Pages artifact or request write-level GitHub Pages permissions.

#### Scenario: Change a developer documentation file in a pull request

- **WHEN** a pull request targeting `main` changes a file under `docs/developer`
- **THEN** the website validation workflow installs dependencies and reports whether the static-site build succeeds

#### Scenario: Change the canonical CLI changelog in a pull request

- **WHEN** a pull request targeting `main` changes `projects/cli/CHANGELOG.md`
- **THEN** the website validation workflow rebuilds the static site with the updated changelog

#### Scenario: Change an unrelated file in a pull request

- **WHEN** a pull request targeting `main` changes none of the configured documentation-site paths
- **THEN** the website validation workflow does not run for that change

### Requirement: Publish affected main-branch builds

The repository SHALL provide a GitHub Pages workflow that runs after pushes to `main` when `docs/developer/**`, `projects/website/src/**`, `projects/cli/CHANGELOG.md`, or the workflow definition changes. The workflow MUST install locked dependencies, build the static site, upload only the generated site output, and deploy that artifact to GitHub Pages using the minimum required Pages permissions. Concurrent deployments MUST preserve an in-progress production deployment.

#### Scenario: Merge a developer documentation change

- **WHEN** a change under `docs/developer` is merged into `main`
- **THEN** the Pages workflow builds the site and publishes its generated output to the repository's GitHub Pages environment

#### Scenario: Merge a website source change

- **WHEN** a change under `projects/website/src` is merged into `main`
- **THEN** the Pages workflow builds and deploys the updated static site

#### Scenario: Merge the canonical CLI changelog

- **WHEN** `projects/cli/CHANGELOG.md` is changed and merged into `main`
- **THEN** the Pages workflow rebuilds and deploys the updated changelog page
