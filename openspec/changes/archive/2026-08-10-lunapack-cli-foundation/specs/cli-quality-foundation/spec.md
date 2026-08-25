## Purpose

Define the repository-level quality and verification contract for the first .NET LunaPack CLI implementation.

## ADDED Requirements

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
