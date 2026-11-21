# cli-changelog-presentation Specification

## Purpose

Present Luna CLI release history on the LunaPack website from the canonical
CLI changelog without maintaining a duplicated website copy.

## Requirements

### Requirement: Publish the canonical CLI changelog page

The website SHALL render `projects/cli/CHANGELOG.md` as a top-level changelog
page at `/changelog` during the static-site build. The canonical file SHALL
remain the only maintained changelog source.

#### Scenario: Visitor opens the changelog

- **WHEN** a reader opens `/changelog`
- **THEN** the page shows the Luna CLI release history from the canonical
  changelog

#### Scenario: Maintainer updates the canonical changelog

- **WHEN** a maintainer changes `projects/cli/CHANGELOG.md`
- **THEN** the next website build renders the updated release history without
  requiring a duplicate website Markdown file

### Requirement: Expose changelog navigation

The main website navigation SHALL include a `Changelog` entry that reaches
the top-level changelog page.

#### Scenario: Visitor uses the main navigation

- **WHEN** a reader opens the LunaPack website
- **THEN** the main navigation provides a direct `Changelog` link

### Requirement: Format CLI release history

The changelog page SHALL use the website theme to distinguish release titles,
release categories, dates, and bulleted release notes. It SHALL not expose the
canonical maintainer instruction used to remind maintainers to update the
`Unreleased` section.

#### Scenario: Reader reviews a released version

- **WHEN** a released version section is rendered
- **THEN** its version, date, categories, and notes remain readable and visually
  separate

#### Scenario: Changelog contains no real unreleased notes

- **WHEN** the `Unreleased` section contains only its maintainer placeholder
- **THEN** the website does not render that placeholder as release information
