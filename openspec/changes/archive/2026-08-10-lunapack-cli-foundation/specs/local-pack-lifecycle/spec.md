## Purpose

Define installation and safe removal of the first versioned pack from a configured local LunaPack source.

## ADDED Requirements

### Requirement: Discover and install the dotnet gitignore pack

LunaPack SHALL provide a versioned `dotnet-gitignore` pack in the repository's local pack source. The pack SHALL declare one managed file that supplies `.gitignore` content suitable for a .NET project.

The `lunapack install dotnet-gitignore` command SHALL discover the pack from configured local sources, create the declared `.gitignore` target, and record the pack identity, resolved version, source, managed target path, and installed content digest in `lunapack.yml`.

#### Scenario: Install the pack from a configured local source

- **WHEN** a user initializes a project, adds the repository local pack source, and runs `lunapack install dotnet-gitignore`
- **THEN** the project contains the pack's `.gitignore` content and `lunapack.yml` records the installed pack and managed file

#### Scenario: Refuse an unknown or unavailable pack

- **WHEN** a user installs a pack that is not present in configured local sources
- **THEN** LunaPack returns a non-success result and does not change project files or `lunapack.yml`

#### Scenario: Refuse to overwrite an existing target

- **WHEN** installation would write `.gitignore` that already exists and is not recorded as managed by the same pack
- **THEN** LunaPack preserves the existing file, does not record the pack, and returns a non-success result

#### Scenario: Refuse duplicate installation

- **WHEN** a user installs `dotnet-gitignore` after it is already recorded in `lunapack.yml`
- **THEN** LunaPack leaves the project unchanged and returns a non-success result

### Requirement: Safely uninstall the dotnet gitignore pack

The `lunapack uninstall dotnet-gitignore` command SHALL remove the managed `.gitignore` target and its pack record when the target content matches the digest recorded at installation. It SHALL not remove content that differs from the recorded digest.

#### Scenario: Uninstall an unmodified managed file

- **WHEN** a user uninstalls `dotnet-gitignore` after its installed `.gitignore` remains unchanged
- **THEN** LunaPack removes `.gitignore` and the corresponding pack record from `lunapack.yml`

#### Scenario: Preserve a modified managed file

- **WHEN** a user modifies the `.gitignore` installed by `dotnet-gitignore` and runs `lunapack uninstall dotnet-gitignore`
- **THEN** LunaPack preserves the modified file and pack record and returns a non-success result

#### Scenario: Reject removal of an uninstalled pack

- **WHEN** a user runs `lunapack uninstall dotnet-gitignore` without a corresponding pack record
- **THEN** LunaPack does not change project files or `lunapack.yml` and returns a non-success result
