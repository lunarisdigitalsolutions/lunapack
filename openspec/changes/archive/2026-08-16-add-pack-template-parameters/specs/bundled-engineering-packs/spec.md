## ADDED Requirements

### Requirement: Publish and install the parameterized MIT license pack

The repository SHALL publish a schema-valid `license-mit` pack that manages
`LICENSE.md`, declares a required `companyName` string parameter, and renders
the MIT copyright holder and current calendar year into the installed file.
The repository's `lunapack.yml` SHALL define `companyName` as `Lunaris Digital
Solutions` and record `license-mit` as an installed root pack.

#### Scenario: Install the repository MIT license from its variable

- **WHEN** the repository installs `license-mit` without an explicit
  `companyName` parameter
- **THEN** its `LICENSE.md` contains `Lunaris Digital Solutions` and the
  current calendar year

#### Scenario: Install the MIT license for another consumer

- **WHEN** a consumer supplies `companyName=Example Corporation` while
  installing `license-mit`
- **THEN** the installed `LICENSE.md` names `Example Corporation` as the
  copyright holder
