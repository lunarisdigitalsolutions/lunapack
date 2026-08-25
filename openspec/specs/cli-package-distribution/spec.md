# cli-package-distribution Specification

## Purpose

Define supported Luna CLI release artifacts and package-manager installation
paths across Windows, Linux, and macOS 64-bit platforms.

## Requirements

### Requirement: Release every supported native target

For every versioned Luna release, the distribution pipeline SHALL produce self-contained native CLI artifacts for `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64` on matching native operating-system and architecture runners. The GitHub Release SHALL contain the matching versioned archive for every target and a checksum file covering the complete archive set.

#### Scenario: Publish all GitHub Release archives

- **WHEN** a `v<semantic-version>` tag starts a successful release
- **THEN** the GitHub Release contains `luna-cli-<version>-win-x64.zip`, `luna-cli-<version>-linux-x64.tar.gz`, `luna-cli-<version>-linux-arm64.tar.gz`, `luna-cli-<version>-osx-x64.tar.gz`, `luna-cli-<version>-osx-arm64.tar.gz`, and checksums for those five archives

#### Scenario: Reject an unsupported manual target

- **WHEN** a manually dispatched release selects a runtime identifier outside the five supported targets
- **THEN** the workflow fails before building or publishing a release artifact

### Requirement: Publish platform-specific npm binary packages

Every versioned Luna release SHALL publish these independent npm packages with the same semantic version as the release: `@lunarisdigitalsolutions/lunapack-win64`, `@lunarisdigitalsolutions/lunapack-linux-x64`, `@lunarisdigitalsolutions/lunapack-linux-arm64`, `@lunarisdigitalsolutions/lunapack-macos-x64`, and `@lunarisdigitalsolutions/lunapack-macos-arm64`. Each package SHALL contain only its matching Luna native binary and SHALL constrain installation to its matching Node `os` and `cpu` values: `win32`/`x64`, `linux`/`x64`, `linux`/`arm64`, `darwin`/`x64`, and `darwin`/`arm64`, respectively.

#### Scenario: Install a platform package on its matching host

- **WHEN** an npm client installs one of the platform packages on the matching operating system and CPU architecture
- **THEN** the package manager installs the package and its Luna binary is available to the entry package

#### Scenario: Reject a platform package on a non-matching host

- **WHEN** an npm client attempts to install a platform package on an operating system or CPU architecture other than its declared target
- **THEN** npm rejects that platform package as incompatible

### Requirement: Provide an npm entry package

Every versioned Luna release SHALL publish `@lunarisdigitalsolutions/lunapack` with the same version as its five platform packages. The entry package SHALL declare all five platform packages as exact-version optional dependencies and SHALL expose the `luna` command. On each supported host, that command SHALL execute the installed matching native Luna binary.

#### Scenario: Install Luna through npm

- **WHEN** a user installs `@lunarisdigitalsolutions/lunapack` on a supported host
- **THEN** the matching optional platform package is installed and the `luna` command runs the platform-native binary

#### Scenario: Run npm Luna on an unsupported host

- **WHEN** a user invokes the npm-installed `luna` command on a host outside the five supported target combinations or without its required optional binary package
- **THEN** the command exits unsuccessfully and identifies the detected platform and supported targets

### Requirement: Provide RID-specific .NET tool packages

Every versioned Luna release SHALL publish a `Lunaris.Lunapack.Luna` NuGet pointer package and one self-contained RID-specific tool package for each supported target, all with the same semantic version. Each Native AOT RID package SHALL be created on its matching native runner, and the release job SHALL validate all five before creating the pointer package. The pointer package SHALL be installable as a .NET tool, expose the `luna` command, and cause the .NET SDK to install the matching RID-specific package on a supported host. NuGet package metadata SHALL be owned outside `Lunapack.Cli.csproj`.

#### Scenario: Install Luna as a .NET tool

- **WHEN** a user installs the `Lunaris.Lunapack.Luna` package with `dotnet tool install` on one of the five supported targets
- **THEN** the .NET SDK installs the matching RID-specific package and the installed `luna` command executes that target's self-contained CLI

#### Scenario: Publish RID packages before the pointer package

- **WHEN** the release action publishes `Lunaris.Lunapack.Luna` packages
- **THEN** it publishes the five RID-specific packages before the pointer package

### Requirement: Publish package-manager distributions after GitHub Release

The release composite action SHALL create the GitHub Release before publishing npm or NuGet packages. It SHALL use the release tag's semantic version for all distribution channels, authenticate to npm and NuGet through trusted OIDC publishing, and fail the workflow when a required package cannot be published.

#### Scenario: Release all distribution channels

- **WHEN** all five target artifacts build successfully for a version tag
- **THEN** the action creates the GitHub Release before publishing the six npm packages, five RID-specific `Lunaris.Lunapack.Luna` packages, and its pointer package with the tag version

#### Scenario: GitHub Release creation fails

- **WHEN** GitHub Release creation fails
- **THEN** the action does not attempt to publish npm or NuGet packages
