# CLI testing strategy

Use this strategy to select, run, and interpret tests for LunaPack CLI changes.
It protects behavior and state boundaries rather than optimizing for one global
coverage percentage.

## Test project responsibilities

| Project                         | Responsibility                                                       |
| ------------------------------- | -------------------------------------------------------------------- |
| `Lunapack.Cli.UnitTests`        | Domain decisions, parsing, formatting, planning, and isolated I/O    |
| `Lunapack.Cli.IntegrationTests` | Real CLI processes, filesystems, Git repositories, and state changes |
| `Lunapack.Cli.SecurityTests`    | Trust, confinement, injection, resource, and filesystem boundaries   |

Keep a test in the narrowest project that can observe the required behavior.
Use integration tests when process dispatch, exit codes, persisted files, or
real operating-system behavior are part of the contract. Security tests may
exercise unit-sized APIs when the protected boundary is the reason the test
exists.

## Design expectations

- Name tests `Scenario_Condition_ExpectedOutcome`.
- Assert observable results, diagnostics, state, and filesystem effects.
- Pair successful state changes with failure and state-preservation cases.
- Parameterize meaningful partitions such as empty, rooted, escaping,
  separator, enum, Boolean, array, and repeated inputs.
- Use a real filesystem for symlink, permission, process, and Git behavior.
- Avoid timing assertions unless a timeout or resource bound is the contract.
- Do not expose production internals, weaken validation, or catch broader
  exceptions to make a test pass.

Every feature adds tests for its successful workflow, parser and validation
boundaries, relevant failure recovery, and repeated execution. Every bug fix
adds a regression test that fails without the fix and protects the reported
behavior.

## Local commands

Run every CLI test suite, one suite, or any combination from the repository
root:

```powershell
./test.ps1
./test.ps1 unit
./test.ps1 unit,int
./test.ps1 -Suite int,security -Configuration Release
```

Restore and validate the strict build first:

```powershell
dotnet restore projects/cli/src/Lunapack.slnx --locked-mode
dotnet build projects/cli/src/Lunapack.slnx --configuration Release --no-restore
dotnet test --solution projects/cli/src/Lunapack.slnx --configuration Release --no-build --no-restore
```

Collect attributable line and branch coverage from instrumentable Debug
binaries. Generated output belongs under `.test-results/` and is ignored:

```powershell
dotnet test --project projects/cli/src/Lunapack.Cli.UnitTests/Lunapack.Cli.UnitTests.csproj --configuration Debug --no-restore --results-directory .test-results/coverage/unit --coverage --coverage-output unit.cobertura.xml --coverage-output-format cobertura
dotnet test --project projects/cli/src/Lunapack.Cli.IntegrationTests/Lunapack.Cli.IntegrationTests.csproj --configuration Debug --no-restore --results-directory .test-results/coverage/integration --coverage --coverage-output integration.cobertura.xml --coverage-output-format cobertura
dotnet test --project projects/cli/src/Lunapack.Cli.SecurityTests/Lunapack.Cli.SecurityTests.csproj --configuration Debug --no-restore --results-directory .test-results/coverage/security --coverage --coverage-output security.cobertura.xml --coverage-output-format cobertura
```

Run CSharpier, .NET style checks, Markdownlint, and Prettier before review:

```powershell
dotnet csharpier check projects
dotnet format style projects/cli/src/Lunapack.slnx --severity info --verify-no-changes --no-restore
npm run lint
npx prettier --check "**/*.{json,css,scss,html,yml,yaml,md}"
```

## Coverage interpretation

Cobertura root attributes report line and branch rates independently. Treat
both as evidence: line coverage shows executed statements, while branch
coverage shows exercised decisions. Reports use Microsoft Code Coverage's
default instrumentation settings.

Review uncovered code by risk. State mutation, rollback, ownership, path
confinement, trust authorization, process invocation, and external-source
handling should approach complete behavioral coverage. Low-risk data accessors
and defensive branches that cannot be induced safely may remain uncovered when
the reason is recorded. Never raise a percentage with redundant tests or tests
that assert implementation details.

## Continuous integration

The CLI action keeps Release validation separate from Debug instrumentation, as
defined by [ADR-0061](../architecture/adr/0061-separate-release-builds-from-instrumented-tests.md).
It runs every test project independently, publishes TRX results through dorny
Test Reporter, and writes the combined ReportGenerator coverage report to the
GitHub job summary. TUnit supplies Microsoft code coverage and emits Cobertura
reports directly; Coverlet is not installed because it is incompatible with
TUnit's Microsoft.Testing.Platform runner.

A public coverage badge is not published. GitHub Actions artifacts have no
stable public metric endpoint, and LunaPack does not depend on an external
coverage service. Revisit a badge only with an accepted service-governance
decision and a stable public endpoint.
