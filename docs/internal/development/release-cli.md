# Release The CLI

Create a CLI release after its consumer-visible implementation commits are on
`main`. The release script validates the changelog and publishes the release
commit and tag in the order consumed by GitHub Actions.

Commits to `main` that change `projects/cli` run the NuGet preview path in the
`CLI: Release` workflow. MinVer derives the next patch preview from the latest
`v` tag, such as `1.2.1-preview.1` after `v1.2.0`. Preview versions are not Git
tags; tagged releases use v-prefixed Semantic Versioning. The preview jobs publish the five
RID-specific tool packages from native-runner artifacts, then publish the
pointer package centrally. They do not publish npm packages or containers and
do not create a GitHub Release. Preview builds retain short-lived workflow
artifacts for three days. Preview NuGet packages include a generated
`CHANGELOG.md` containing only the canonical `Unreleased` section.

Both paths call the reusable release action. Its `release-type` input accepts
`stable` or `preview` and defaults to `stable`. The action maps stable to the
GitHub, container, npm, and NuGet channels and preview to NuGet. A channel
dispatcher validates that selection and calls one composite publisher per
channel. Before stable dispatch, the release action prepares the archives,
release notes, checksums, native files, and npm packages shared by GitHub,
container, and npm. NuGet downloads and validates its RID packages inside the
NuGet publisher. Stable and preview runs share one plan, native build matrix,
and release job. For previews, the release job runs only NuGet staging,
authentication, and ordered publication.

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
When the transfer is confirmed, the script continues through the release
workflow. Otherwise, the new version receives a release-notes placeholder; fill
in the new release section and rerun the script. This flow applies to derived and explicit
versions; an explicit version must be greater than the current changelog
version.

The script requires checked-out `main` to match `origin/main`, verifies that
the release version is newer than the following changelog version, and rejects
existing local or remote `vx.x.x` tags. It asks before fetching, creating the
changelog section, creating the release commit, creating the annotated tag, and
one atomic push. Declining a dependent action stops the remaining release
workflow. The release commit contains only `projects/cli/CHANGELOG.md` and
uses `release: Release version x.x.x`. If the changelog has no changes, the
script skips the release commit and pushes only the tag. Otherwise, it creates
the release commit and tag locally, then atomically pushes `main` and the tag in
one command. The pushed `vx.x.x` tag starts the Luna release
workflow. The GitHub Release title is `Luna vx.x.x` and attaches a
`CHANGELOG.md` containing only the matching version section. It publishes
archives for `win-x64`, `linux-x64`,
`linux-arm64`, `osx-x64`, and `osx-arm64`, plus `SHA256SUMS.txt`. Builds run on
matching native GitHub-hosted runners: Windows x64, Linux x64/Arm64, and macOS
x64/Arm64. Each runner also creates its Native AOT RID-specific NuGet package.
After the shared build action completes, the Windows x64, Linux x64, and macOS
x64 build entries execute the complete pack lifecycle sanity check against
their local publish output. The Arm64 build entries skip this workflow-owned
step but retain build and package validation. Any x64 sanity failure fails the
aggregate build job and blocks release.
The release action rejects a missing, duplicate, or unexpected archive before
dispatch. The NuGet publisher performs the equivalent RID package validation
when its channel starts.

After GitHub Release creation, the dispatcher builds and publishes the Linux
x64 container, publishes five constrained npm binary packages followed by
`@lunarisdigitalsolutions/lunapack`, then publishes the five RID-specific
`Lunaris.Lunapack.Luna` NuGet tool packages followed by its pointer package.
Stable npm and NuGet packages include the complete CLI changelog. The container
publisher uses the staged Linux binary and pushes version plus `latest` or
`next` tags to
`ghcr.io/lunarisdigitalsolutions/lunapack`. Configure npm trusted publishing for
this workflow and package, configure NuGet trusted publishing, and set the
`NUGET_USER` repository Actions secret before tagging. Stable npm releases use
`latest`; prerelease versions use `next`. Release tags must omit Semantic
Versioning build metadata because OCI tags cannot contain `+` and every channel
uses one unchanged version. A rerun downloads an existing GitHub Release and
requires its five archives, checksum manifest, changelog asset, and release
notes to match local staging exactly. It then skips an already-published npm
version and uses NuGet duplicate skipping to resume an interrupted registry
publication.

The reusable release action accepts `dry-run: 'true'`. Dry-run execution
downloads and validates archives, stages npm packages, and builds NuGet
pointer metadata after validating native RID packages. It also builds the
container locally, but skips GitHub Release creation, registry authentication,
and all publishing steps. Use this mode from a temporary validation workflow
before the first public release; do not publish from a pull request.

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
environment. Store the NuGet.org username associated with those policies as a
repository Actions secret named `NUGET_USER`. `NuGet/login` exchanges OIDC for
a temporary API key; do not create a `NUGET_API_KEY` secret.

GHCR uses the job-scoped `GITHUB_TOKEN`; no registry secret is required. Keep
`packages: write` on the release job, allow GitHub Actions to create organization
packages, and make `ghcr.io/lunarisdigitalsolutions/lunapack` public after its
first push. Confirm the package remains linked to this repository so workflow
access and provenance are visible.

## Recover From A Tag Push Failure

If the atomic push fails, the release commit and local tag remain available.
Resolve the remote failure, then rerun the same push command:

```powershell
git push --atomic origin main v1.0.1
```

If the remote has already accepted `main` but not the tag, push the existing tag
alone after verifying the release commit is present on `origin/main`:

```powershell
git push origin v1.0.1
```
