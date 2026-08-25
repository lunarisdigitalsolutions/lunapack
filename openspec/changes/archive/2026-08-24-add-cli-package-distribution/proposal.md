## Why

Luna releases currently provide only Linux x64 and Windows x64 archives from
GitHub Releases. Users need package-manager installation and native binaries
for supported 64-bit Linux, Windows, and macOS platforms without changing the
Ubuntu release runner.

## What Changes

- Build and release five self-contained CLI targets on `ubuntu-latest`:
  `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`.
- Attach one ZIP archive for Windows and four `tar.gz` archives for Unix-like
  targets to every tagged GitHub Release, with checksums for the complete set.
- Publish five independent npm platform packages under
  `@lunarisdigitalsolutions`, each constrained by its supported Node `os` and
  `cpu` values and containing its matching Luna binary.
- Publish `@lunarisdigitalsolutions/lunapack` as the npm entry package. It uses
  the platform packages as optional dependencies and exposes the `luna`
  command by selecting the installed native package, following the esbuild
  package-family model.
- Publish `Lunaris.Lunapack.Luna` as one .NET tool pointer package plus five
  self-contained RID-specific tool packages. Keep NuGet package metadata in an
  imported package definition rather than directly in `Lunapack.Cli.csproj`.
- Extend the CLI release composite action to publish the NuGet and npm
  packages after the GitHub Release succeeds, using repository secrets for
  registry authentication.
- Document GitHub Release, npm, and .NET tool installation and maintain the
  internal release process. Record the durable distribution architecture
  decision in an ADR and add consumer-visible installation channels to the
  changelog.

## Capabilities

### New Capabilities

- `cli-package-distribution`: Defines supported platform artifacts and package-manager installation for Luna CLI releases.

### Modified Capabilities

- None.

## Impact

- Affected automation: `.github/workflows/cli.yml`, the CLI build and release
  composite actions, and any new packaging scripts or manifests.
- Affected build configuration: CLI runtime identifier declarations and
  release artifact validation.
- Affected publishing systems: GitHub Releases, npm, and NuGet.org.
- Affected documentation: `docs/developer/installation.md`,
  `docs/internal/development/release-cli.md`, the CLI product requirements,
  and a new accepted ADR under `docs/internal/architecture/adr`.
- Affected consumer-facing release notes: `CHANGELOG.md`.
