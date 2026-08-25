## Context

The existing release workflow builds `linux-x64` and `win-x64`,
uploads one archive per target, and validates only those archives before
creating a GitHub Release. `Lunapack.Cli.csproj` owns those two runtime
identifiers but has no package metadata. The repository has no npm or NuGet
distribution package source. See [proposal.md](proposal.md) for motivation and
[the distribution spec](specs/cli-package-distribution/spec.md) for the
consumer contract.

## Goals / Non-Goals

**Goals:**

- Use one build matrix with a matching native runner for each supported .NET RID.
- Derive release assets and npm packages from the same native publish outputs.
- Use the .NET SDK's RID-specific tool packaging to select the NuGet payload.
- Use npm's `os`, `cpu`, and optional-dependency behavior to select native
  payloads.
- Provide one `Luna` .NET tool without package metadata in the CLI project.
- Make publish ordering and partial-release recovery explicit.

**Non-Goals:**

- Support 32-bit, musl, Windows Arm64, or other operating-system targets.
- Change the CLI command or pack lifecycle contracts.
- Change tag format, MinVer derivation, or publish npm and NuGet packages from
  branch, pull-request, or manually dispatched builds.

## Decisions

### Build all supported RIDs on matching native runners

The target map in `cli.yml` will add `linux-arm64`, `osx-x64`, and
`osx-arm64` to the existing `linux-x64` and `win-x64`. Each target uses a
matching native operating-system and architecture runner. Tag events select all
five. Manual dispatch validates the same allow-list and can select a subset for
build verification without publishing.

The CLI project retains only runtime compilation settings necessary for those
targets; it receives no package identity, metadata, or package-layout
configuration. The existing build composite still produces one target per
matrix entry. Windows produces ZIP; Unix-like targets produce `tar.gz`. The
same runner creates the Native AOT RID-specific NuGet package from its publish
output. The release composite validates exactly five versioned archive names
and five RID packages before checksumming or releasing them, then creates the
RID-selecting pointer package centrally.

Alternative considered: cross-compile every target on Ubuntu. Rejected because
Native AOT compatibility must be validated on each target family.

### Generate npm packages from release staging

After validation and GitHub Release creation, the release composite extracts
the five archives into temporary staging. Package templates outside the CLI
project generate five binary packages, each containing only one native binary
and matching `os`/`cpu` metadata, and an entry package with exact-version
optional dependencies on all five.

The entry package includes a Node `luna` launcher. It maps
`process.platform` and `process.arch` to the matching optional dependency,
starts its binary, forwards arguments and exit status, and reports an error for
unsupported hosts or omitted optional dependencies. The published suffixes are
`win64`, `linux-x64`, `linux-arm64`, `macos-x64`, and `macos-arm64`; their Node
platform values are `win32`, `linux`, and `darwin` as applicable.

This mirrors esbuild's platform-package model and makes the GitHub assets and
registry payloads originate from one validated binary set. Alternative
considered: one npm package containing every binary. Rejected because it
downloads unneeded payloads and loses npm platform validation.

### Package .NET tools with SDK-managed RID selection

.NET SDK 10 packages a tool with `ToolPackageRuntimeIdentifiers` as one
RID-agnostic pointer package and one self-contained package for every supported
RID. `dotnet tool install` resolves the matching package, so Luna does not need
a managed launcher, staged native payloads, or its own platform selector.

`Lunapack.ToolPackage.props` supplies the `Lunaris.Lunapack.Luna` identity,
version metadata, command name, and the five tool RIDs only when the release
pack command enables it. `Lunapack.Cli.csproj` imports that definition but
contains no package metadata. The CLI project remains the executable packaged
for every RID.

Alternative considered: a custom managed launcher that selects binaries staged
from GitHub release archives. Rejected because SDK 10 provides native RID
resolution and produces smaller platform-specific tool packages.

### Publish in ordered, recoverable phases

The release action strips the `v` prefix from a validated tag and uses that
semantic version for archives, all npm `package.json` files, and the NuGet
package. Stable npm releases use the `latest` tag; prereleases use `next`.

The action phases are: validate and stage archives; create GitHub Release;
stage package inputs; publish five npm platform packages; publish the npm entry
package; publish the five NuGet RID packages; publish the NuGet pointer package.
`NPM_TOKEN` and `NUGET_API_KEY` repository
secrets supply registry credentials. A registry failure leaves the completed
GitHub Release visible, fails the workflow with phase and package identity, and
allows a rerun to skip only an already-published identical immutable version.

Alternative considered: publish registries before GitHub Release. Rejected
because users could install packages before release assets and notes exist, and
the required ordering is GitHub Release first.

### Document and record the distribution boundary

Implementation will record the five-target distribution boundary in ADR-0038.
Developer installation documentation will separate GitHub Release, npm, and
.NET tool paths. Internal release guidance will cover identity requirements,
order, rerun behavior, and expanded assets. The product requirement and
changelog will describe the consumer-visible distribution channels.

## Risks / Trade-offs

- [A self-contained RID publish lacks a runtime pack on Ubuntu] -> Add each
  RID to CI and verify its staged payload before release creation.
- [npm clients omit optional dependencies] -> The launcher detects the missing
  binary and reports reinstall guidance instead of selecting another target.
- [Registry publication stops partway through] -> Preserve GitHub Release-first
  order, identify the failed package, and recover by confirming immutable
  versions already published before retrying remaining phases.
- [The tool host selects a wrong binary] -> Unit-test RID mapping and argument
  forwarding, then validate staged tool layout and execution.
- [Registry secrets are missing] -> Fail before publication with configuration
  guidance without logging credential values.

## Migration Plan

1. Expand the target map and exact artifact validation while preserving current
   Linux and Windows x64 archive names.
2. Add npm templates, the .NET tool host/package definition, staging logic,
   and local package-layout tests.
3. Add GitHub Release-first npm and NuGet publication with recovery checks.
4. Configure npm and NuGet registry access before the first tagged release.
5. Publish installation and release documentation with the first new-channel
   release.

Rollback stops later phases and retains the GitHub Release for inspection.
Published package versions are immutable; recovery validates completed package
metadata and reuses the same version and staged artifacts instead of replacing
published content.
