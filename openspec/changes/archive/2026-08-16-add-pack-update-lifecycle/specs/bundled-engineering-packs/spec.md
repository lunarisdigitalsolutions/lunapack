## MODIFIED Requirements

### Requirement: Publish bundled engineering convention packs

The repository SHALL publish schema-valid versioned packs in its local pack
source for `dotnet-gitignore`, `gitignore-general`, `dotnet-sdk-10`,
`dotnet-editorconfig`, `dotnet-csharpier-tool`, `dotnet-quality-baseline`, and
`madr-adr-template`. Each pack SHALL provide a concise description suitable for
catalog discovery and SHALL manage only complete files.

The packs SHALL target `.gitignore`, `global.json`, `.editorconfig`,
`dotnet-tools.json`, the pair `Directory.Build.props` and
`Directory.Packages.props`, and `docs/adr/template.md`, respectively. The
`dotnet-gitignore` and `gitignore-general` packs SHALL declare section merge
strategies for their `.gitignore` targets. `dotnet-sdk-10` and
`dotnet-csharpier-tool` SHALL declare JSON merge strategies. A consumer of
`madr-adr-template` SHALL create its `docs/adr` target directory before
installation.

#### Scenario: Discover the bundled catalog

- **WHEN** a consumer configures the repository local pack source and runs `lunapack discover`
- **THEN** the result includes each bundled engineering convention pack with its available version and description

#### Scenario: Search bundles by engineering concern

- **WHEN** a consumer searches the configured source for an engineering concern present in a bundled pack's ID or description
- **THEN** LunaPack returns the matching bundled pack releases according to the catalog search contract

#### Scenario: Merge generic and .NET ignore sections

- **WHEN** a consumer installs both `gitignore-general` and `dotnet-gitignore`
- **THEN** each pack manages its marked `.gitignore` section without replacing
  the other pack's section

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
