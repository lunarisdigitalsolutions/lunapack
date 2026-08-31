# bundled-engineering-packs Specification

## Purpose

Publish reusable engineering conventions from LunaPack's local source and let the repository exercise installation ownership as a consumer.

## Requirements

### Requirement: Publish bundled engineering convention packs

The repository SHALL publish schema-valid versioned managed-file packs in its
local pack source for `dotnet-gitignore`, `gitignore-baseline`,
`dotnet-sdk-10`, `editorconfig-baseline`, `dotnet-csharp-editorconfig`,
`csharpier`, `dotnet-build-config`, `dotnet-central-package-management`, and
`madr-template`. It SHALL also publish the `dotnet-editorconfig` and
`dotnet-project` composite packs with exact references to their component
packs. Each pack SHALL provide a concise description suitable for catalog
discovery.

The managed-file packs SHALL target `.gitignore`, `global.json`,
`.editorconfig`, `dotnet-tools.json`, `Directory.Build.props`,
`Directory.Packages.props`, and `docs/adr/template.md` according to their
declared concerns. The `dotnet-gitignore` and `gitignore-baseline` packs SHALL
declare section merge strategies for their `.gitignore` targets.
`editorconfig-baseline` and `dotnet-csharp-editorconfig` SHALL declare section
merge strategies for `.editorconfig`. `dotnet-sdk-10` and `csharpier` SHALL
declare JSON merge strategies. A consumer of `madr-template` SHALL create its
`docs/adr` target directory before installation.

#### Scenario: Discover the bundled catalog

- **WHEN** a consumer configures the repository local pack source and runs `luna discover`
- **THEN** the result includes each bundled engineering convention pack with its available version and description

#### Scenario: Search bundles by engineering concern

- **WHEN** a consumer searches the configured source for an engineering concern present in a bundled pack's ID or description
- **THEN** LunaPack returns the matching bundled pack releases according to the catalog search contract

#### Scenario: Merge generic and .NET ignore sections

- **WHEN** a consumer installs both `gitignore-baseline` and `dotnet-gitignore`
- **THEN** each pack manages its marked `.gitignore` section without replacing
  the other pack's section

### Requirement: Configure the repository as an installed consumer

The repository SHALL contain a schema-valid `lunapack.yml` that configures its `projects/packs` directory as a local source and records every bundled pack as installed. The repository SHALL install the generic managed-file content before adding any repository-specific overlay content manually.

#### Scenario: Install the repository's own packs

- **WHEN** a developer removes the existing managed targets, creates `docs/adr`, and installs each bundled pack from the repository root
- **THEN** `lunapack.yml` records every bundled pack and its managed file digests

#### Scenario: Preserve the installed generic baseline

- **WHEN** a developer manually restores repository-specific content after installing a generic pack
- **THEN** the repository keeps the overlay content and the corresponding managed file no longer matches its recorded digest

### Requirement: Publish and install the parameterized MIT license pack

The repository SHALL publish a schema-valid `license-mit` pack that manages
`LICENSE.md` with a `copy/overwrite` strategy, declares a required `companyName`
string parameter, and renders the MIT copyright holder and current calendar
year into the installed file. The repository's `lunapack.yml` SHALL define
`companyName` as `Lunaris Digital Solutions` and record `license-mit` as an
installed root pack.

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
