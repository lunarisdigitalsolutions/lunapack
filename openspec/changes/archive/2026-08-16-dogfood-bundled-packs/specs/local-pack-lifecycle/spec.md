## MODIFIED Requirements

### Requirement: Discover and install bundled managed-file packs

LunaPack SHALL provide versioned `dotnet-gitignore`, `dotnet-sdk-10`,
`dotnet-editorconfig`, `dotnet-csharpier-tool`, `dotnet-quality-baseline`, and
`madr-adr-template` packs in the repository's local pack source. Each pack
SHALL declare one or more managed files containing complete reusable
engineering-convention content.

The `lunapack install <pack-id>` command SHALL discover each bundled pack from
configured local sources, create each declared target when it does not already
exist, and record the pack identity, resolved version, source, managed target
paths, and installed content digests in `lunapack.yml`.

#### Scenario: Install a single-file bundled pack from a configured local source

- **WHEN** a user initializes an otherwise empty project, adds the repository
  local pack source, and runs `lunapack install dotnet-editorconfig`
- **THEN** the project contains the pack's `.editorconfig` content and
  `lunapack.yml` records the installed pack and managed file

#### Scenario: Install the multi-file quality baseline

- **WHEN** a user installs `dotnet-quality-baseline` into a project where both
  declared targets are absent
- **THEN** LunaPack creates `Directory.Build.props` and
  `Directory.Packages.props` and records both managed files for the installed
  pack

#### Scenario: Install a documentation template into an existing directory

- **WHEN** a user creates `docs/adr`, configures the repository local source,
  and runs `lunapack install madr-adr-template`
- **THEN** LunaPack creates `docs/adr/template.md` and records it as the pack's
  managed file

#### Scenario: Refuse an unknown or unavailable pack

- **WHEN** a user installs a pack that is not present in configured local
  sources
- **THEN** LunaPack returns a non-success result and does not change project files
  or `lunapack.yml`

#### Scenario: Refuse to overwrite an existing target

- **WHEN** installation would write a declared target that already exists and
  is not recorded as managed by the same pack
- **THEN** LunaPack preserves the existing file, does not record the pack, and
  returns a non-success result

#### Scenario: Refuse duplicate installation

- **WHEN** a user installs a bundled pack after it is already recorded in
  `lunapack.yml`
- **THEN** LunaPack leaves the project unchanged and returns a non-success result

### Requirement: Safely uninstall an unchanged managed-file pack

The `lunapack uninstall <pack-id>` command SHALL remove every managed target and
its pack record when every target content digest matches the digest recorded at
installation. It SHALL not remove any managed content that differs from its
recorded digest.

#### Scenario: Uninstall an unmodified single-file pack

- **WHEN** a user uninstalls `dotnet-gitignore` after its installed
  `.gitignore` remains unchanged
- **THEN** LunaPack removes `.gitignore` and the corresponding pack record from
  `lunapack.yml`

#### Scenario: Uninstall an unmodified multi-file pack

- **WHEN** a user uninstalls `dotnet-quality-baseline` after both managed files
  remain unchanged
- **THEN** LunaPack removes both managed files and the corresponding pack record
  from `lunapack.yml`

#### Scenario: Preserve a modified managed file

- **WHEN** a user modifies a file installed by a bundled pack and runs
  `lunapack uninstall <pack-id>`
- **THEN** LunaPack preserves the modified file and pack record and returns a
  non-success result

#### Scenario: Reject removal of an uninstalled pack

- **WHEN** a user runs `lunapack uninstall <pack-id>` without a corresponding
  pack record
- **THEN** LunaPack does not change project files or `lunapack.yml` and returns a
  non-success result
