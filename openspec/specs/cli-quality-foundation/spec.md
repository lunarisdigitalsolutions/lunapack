# cli-quality-foundation Specification

## Purpose

Define the repository-level quality and verification contract for the first .NET Luna CLI implementation.

## Requirements

### Requirement: Provide the CLI solution layout

The repository SHALL place the first .NET 10 CLI solution and production projects beneath `projects/cli/src/`. Test projects SHALL remain separate from production projects and be runnable through the solution's standard test command.

#### Scenario: Build the solution

- **WHEN** a contributor builds the CLI solution with the supported .NET SDK
- **THEN** the CLI and all test projects compile successfully

### Requirement: Verify every CLI behavior

The solution SHALL use TUnit for unit and integration tests. Unit tests SHALL cover command parsing and all command behavior in scope. Integration tests SHALL execute the built CLI in isolated temporary project directories and cover initialization, local-source registration, installation, and uninstallation of `dotnet-gitignore`.

#### Scenario: Run the CLI test suite

- **WHEN** a contributor runs the solution test command
- **THEN** unit and integration tests verify each supported CLI command and its failure cases

### Requirement: Apply C# quality conventions

The solution SHALL enforce CSharpier formatting and Roslyn analyzer diagnostics. Test class and method names SHALL use the `Scenario_Condition_ExpectedOutcome` pattern, and test source SHALL not contain Arrange-Act-Assert narration comments.

#### Scenario: Check project quality

- **WHEN** a contributor runs the documented formatting and build checks
- **THEN** unformatted source or analyzer violations cause a non-success result

### Requirement: Produce deterministic MinVer-versioned CI builds

The root `Directory.Build.props` and the `dotnet-build-config` pack template
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

- **WHEN** a consumer installs `dotnet-build-config`
- **THEN** its generated `Directory.Build.props` provides the same CI
  determinism and restore policy as the repository root file
