# developer-documentation-site Specification

## Purpose

Provide a browsable developer website that explains LunaPack and publishes the
repository's existing developer documentation without creating a second source
of truth.

## Requirements

### Requirement: Present a developer-oriented landing page

The website SHALL present a responsive landing page for software architects, engineering leads, senior developers, platform teams, and DevOps engineers. It MUST state the engineering outcome before implementation details and include concise sections for the problem, solution, workflow, command-line example, benefits, and a call to read the developer documentation. Command examples MUST describe behavior supported by the current developer documentation.

#### Scenario: Visit the site root

- **WHEN** a reader opens the website root
- **THEN** they can understand LunaPack's pack-management outcome and reach the developer documentation from the landing page

#### Scenario: Inspect an onboarding example

- **WHEN** a reader reaches the landing-page example
- **THEN** it shows a supported LunaPack CLI workflow without claiming unavailable behavior

### Requirement: Publish canonical developer documentation

The website SHALL render the Markdown hierarchy under `docs/developer` as its developer documentation. Those Markdown files MUST remain the canonical source and MUST NOT be copied into the website project. The rendered documentation MUST provide navigation for the existing CLI and pack guidance.

#### Scenario: Browse a developer guide

- **WHEN** a reader opens the documentation section
- **THEN** they can navigate to the CLI guide and the pack guide from the rendered `docs/developer` content

#### Scenario: Update a developer-documentation source file

- **WHEN** a maintainer changes a Markdown file under `docs/developer`
- **THEN** the next website build includes that change without requiring a copied website-documentation file to be updated

### Requirement: Keep website dependencies reproducible and local

The website SHALL be an npm project rooted at `projects/website/src`. All direct npm dependencies and npm metadata for the website MUST be located in that directory, use exact stable version numbers, and have a committed lock file. A clean dependency installation followed by the website build command MUST produce the static site without relying on root npm dependencies.

#### Scenario: Build from a clean website dependency tree

- **WHEN** a contributor installs dependencies from the committed website lock file and runs the documented build command in `projects/website/src`
- **THEN** the static documentation site builds successfully
