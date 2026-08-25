## ADDED Requirements

### Requirement: Produce deterministic MinVer-versioned CI builds

The root `Directory.Build.props` and the `dotnet-build-props` pack template
SHALL configure deterministic CI builds, Source Link, locked restore, and
exclusion of source-revision data from the informational version. Both SHALL
set `MinVerDefaultPreReleaseIdentifiers` to `preview`. The CLI project SHALL
reference the MinVer build package privately, and the repository tool manifest
SHALL provide the MinVer command-line tool.

A CI build without a version override SHALL derive its metadata from MinVer.
When a workflow supplies a version override, the MinVer build package SHALL use
that value consistently for the CLI assembly, file, informational, and package
versions. Tagged release builds use the semantic suffix of their `releases/*`
tag as that override.

#### Scenario: Build the CLI in CI

- **WHEN** CI builds the CLI from a Git history whose MinVer result is
  `1.0.42-preview.3`
- **THEN** the build uses deterministic and locked-restore settings and the
  resulting CLI metadata consistently derives from `1.0.42-preview.3`

#### Scenario: Restore and calculate the CLI version

- **WHEN** CI restores repository-local .NET tools and invokes `minver` with
  its default verbosity
- **THEN** the command's standard output supplies the calculated semantic
  version used by builds that do not supply a version override

#### Scenario: Apply the build-properties pack

- **WHEN** a consumer installs `dotnet-build-props`
- **THEN** its generated `Directory.Build.props` provides the same CI
  determinism and restore policy as the repository root file
