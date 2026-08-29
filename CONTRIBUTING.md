# Contributing to LunaPack

Thank you for improving LunaPack. This guide covers repository changes to the
CLI, schemas, maintained packs, automation, and documentation.

By participating, you agree to follow the
[Code of Conduct](CODE_OF_CONDUCT.md). Use [Support](SUPPORT.md) for usage
questions and [Security](SECURITY.md) for vulnerability reports.

## Before you start

- Search existing issues and pull requests for related work.
- Open an issue before substantial public-interface or architecture changes.
- Do not put credentials, customer data, private URLs, or personal information
  in issues, tests, examples, commits, or generated artifacts.

## Set up the repository

Install the .NET SDK selected by `global.json` and a supported Node.js release.
From the repository root, restore pinned dependencies and local tools:

```powershell
dotnet restore projects/cli/src/Lunapack.slnx --locked-mode
npm ci
```

## Validate changes

Run checks for the surface you changed. Before requesting review, run the full
local baseline:

```powershell
dotnet build projects/cli/src/Lunapack.slnx --configuration Release
dotnet test --solution projects/cli/src/Lunapack.slnx --configuration Release --no-build
npm run lint
npm run --prefix projects/website build
```

Changes to pack manifests must validate against `projects/schema/pack.schema.json`
and include focused lifecycle tests when behavior changes. Changes to public
CLI behavior must update help, tests, schemas, examples, developer documentation,
and `CHANGELOG.md` when consumers can observe the change.

Public documentation changes must follow the
[external developer documentation standard](docs/internal/development/external-documentation-standard.md).

## Pull requests

- Keep each pull request focused and explain the user-visible reason for it.
- Add or update tests for changed behavior.
- Record durable architectural decisions as ADRs under
  `docs/internal/architecture/adr`.
- Keep generated output, package contents, test results, and local configuration
  out of commits.
- Do not publish packages, images, releases, or the website from a contribution.

Maintainers merge changes after required checks pass and review feedback is
resolved. Release versions come from release tags; contributors do not edit
package versions for ordinary pull requests.
