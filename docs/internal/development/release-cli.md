# Release The CLI

Create a CLI release after its consumer-visible implementation commits are on
`main`. The release script validates the changelog and publishes the release
commit and tag in the order consumed by GitHub Actions.

## Prerequisites

- Commit and push all implementation changes to `origin/main`.
- Leave the Git index empty. Unstaged changes outside
  `projects/cli/CHANGELOG.md` remain uncommitted by the script.
- Run the script with PowerShell Core (`pwsh`), not Windows PowerShell.
- Restore the repository-local .NET tools so the script can run MinVer:

```powershell
dotnet tool restore
```

## Create The Release

Run the script from the repository root without a version. It asks whether the
release is a breaking change, new functionality, or a small change, then asks
MinVer for the corresponding major, minor, or patch version:

```powershell
./scripts/New-Release.ps1
```

If MinVer cannot derive a version newer than the newest changelog version, the
script derives the selected increment from that changelog version. This lets
the repository move forward from historical tags that MinVer cannot parse.

Pass a stable three-part semantic version to use an explicit release version:

```powershell
./scripts/New-Release.ps1 -Version 1.0.1
```

When a derived version has no changelog section, the script offers to add
`## Version x.x.x - YYYY-MM-DD`, then asks whether to copy the `Unreleased`
entries into it when that section has release notes. The default `Update this
section before creating a release tag.` text remains in `Unreleased` and does
not trigger this prompt. Choose the transfer option after recording release
notes below that text. Release notes describe externally observable changes for
CLI consumers; exclude independently versioned pack packages, documentation,
CI, build, test, and release-process work.
Otherwise, the new version receives a release-notes placeholder. The script
stops after either action. Fill in the new release section and rerun the
script. This flow applies to derived and explicit
versions; an explicit version must be greater than the current changelog
version.

The script requires checked-out `main` to match `origin/main`, verifies that
the release version is newer than the following changelog version, and rejects
existing local or remote `vx.x.x` tags. It asks before fetching,
creating the changelog section, creating the release commit, pushing `main`,
creating the annotated tag, and pushing that tag. Declining a dependent action
stops the remaining release workflow. The release commit contains only
`projects/cli/CHANGELOG.md` and uses `chore: Release version x.x.x`. If the
changelog has no changes, the script skips the release commit and `main` push
before creating the tag. The pushed `vx.x.x` tag starts the Luna release
workflow. The GitHub Release title is `Luna vx.x.x` and attaches a
`CHANGELOG.md` containing only the matching version section. It publishes
archives for `win-x64`, `linux-x64`,
`linux-arm64`, `osx-x64`, and `osx-arm64`, plus `SHA256SUMS.txt`. Builds run on
matching native GitHub-hosted runners: Windows x64, Linux x64/Arm64, and macOS
x64/Arm64. Each runner also creates its Native AOT RID-specific NuGet package.
The release job rejects a missing, duplicate, or unexpected archive or RID
package before creating the release.

After GitHub Release creation, the release action publishes five constrained
npm binary packages, then `@lunarisdigitalsolutions/lunapack`, then the five
RID-specific `Lunaris.Lunapack.Luna` NuGet tool packages, and finally its
pointer package. Every npm and NuGet package includes the complete CLI
changelog. The action also builds a Linux x64 image from the downloaded Linux
archive and pushes version plus `latest` or `next` tags to
`ghcr.io/lunarisdigitalsolutions/lunapack`. Configure npm trusted publishing for
this workflow and package, configure NuGet trusted publishing, and set the
non-secret `NUGET_USER` repository variable before tagging. Stable npm releases
use `latest`; prerelease versions use `next`. Release tags must omit Semantic
Versioning build metadata because OCI tags cannot contain `+` and every channel
uses one unchanged version. A rerun downloads an existing GitHub Release and
requires its five archives, checksum manifest, changelog asset, and release notes
to match local staging exactly. It then skips an already-published npm version
and uses NuGet duplicate skipping to resume an interrupted registry publication.

The reusable release action accepts `dry-run: 'true'`. Dry-run execution
downloads and validates archives, stages npm packages, and builds NuGet
pointer metadata after validating native RID packages, but skips GitHub Release
creation, registry authentication, and all publishing steps. Use this mode from
a temporary validation workflow before the first public release; do not publish
from a pull request.

## Configure Package Publishers

The release workflow is `.github/workflows/cli.yml` in the
`lunarisdigitalsolutions/lunapack` repository. It uses GitHub Actions OIDC and
must not store npm or NuGet publishing tokens.

For npm, create or claim the `@lunarisdigitalsolutions` organization and grant
the publishing maintainer access to it. Configure a GitHub Actions trusted
publisher for each package produced by the workflow:

- `@lunarisdigitalsolutions/lunapack`
- `@lunarisdigitalsolutions/lunapack-win64`
- `@lunarisdigitalsolutions/lunapack-linux-x64`
- `@lunarisdigitalsolutions/lunapack-linux-arm64`
- `@lunarisdigitalsolutions/lunapack-macos-x64`
- `@lunarisdigitalsolutions/lunapack-macos-arm64`

Use organization `lunarisdigitalsolutions`, repository `lunapack`, workflow
filename `cli.yml`, and no GitHub environment. npm provenance is requested by
every publish command. If npm requires an initial package publication before a
trusted publisher can be configured, perform that bootstrap from a protected
maintainer environment, then remove the temporary token and use OIDC only.

For NuGet.org, create trusted-publishing policies for the `Lunaris.Lunapack.Luna`
pointer package and its five RID package IDs. Use owner
`lunarisdigitalsolutions`, repository `lunapack`, workflow `cli.yml`, and no
environment. Set repository Actions variable `NUGET_USER` to the NuGet.org
username associated with those policies. `NuGet/login` exchanges OIDC for a
temporary API key; do not create a `NUGET_API_KEY` secret.

GHCR uses the job-scoped `GITHUB_TOKEN`; no registry secret is required. Keep
`packages: write` on the release job, allow GitHub Actions to create organization
packages, and make `ghcr.io/lunarisdigitalsolutions/lunapack` public after its
first push. Confirm the package remains linked to this repository so workflow
access and provenance are visible.

## Recover From A Tag Push Failure

If the `main` push succeeds but the tag push fails, do not rerun the script:
the release commit and local tag already exist. Resolve the remote failure,
then push the existing tag:

```powershell
git push origin v1.0.1
```
