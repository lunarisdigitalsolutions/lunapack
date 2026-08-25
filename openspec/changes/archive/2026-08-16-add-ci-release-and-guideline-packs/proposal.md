## Why

LunaPack's bundled packs cannot currently install a file at a caller-selected
directory, leaving existing repository guidance unmanaged. The CLI also lacks
repeatable CI that validates, packages, checksums, tags, and releases its
native executable artifacts.

## What Changes

- Add first-party `clean-code-guidelines` and `csharp-guidelines` documentation
  packs. Their source material is
  `docs/internal/development/coding-guidelines/clean-code.md` and
  `docs/internal/development/coding-guidelines/csharp.md`; the pack content is
  not duplicated in these planning artifacts.
- Extend `lunapack install` with optional `--destination <relative-directory>` and
  `--adopt-existing` options. A destination relocates only the directly
  requested pack's managed files. Persist the selected destination in both
  `lunapack.yml` and `lunapack-lock.yml`; permit opt-in adoption only when each
  existing target has the exact pack-content digest.
- Install both new guideline packs in this repository at their existing
  documentation directory through the new destination and adoption behavior.
- Add generic .NET, CLI build, and CLI release composite actions. The
  `releases/*` tag workflow builds Windows and Linux x86/x64 by default. Manual
  dispatch selects supported Linux, Windows, macOS, x64, x86, and ARM64 target
  combinations without creating a release. A separate pull-request workflow
  runs the Linux x64 validation build only and publishes no artifacts.
- Add the `MinVer` build package and restored `minver-cli` local tool. Derive
  application metadata, release asset names, and release versions from the
  version calculated from Git tags, with `preview` as the default prerelease
  identifier.
- Add a tag-triggered release action that uses the triggering tag as the
  version, creates a GitHub Release, publishes every distribution archive, and
  attaches a SHA-256 checksum manifest and root changelog. The workflow does
  not create a tag.
- Add root `CHANGELOG.md` and stage it as a GitHub Release asset.
- Align the repository root and `dotnet-build-props` pack templates with
  deterministic CI, Source Link, locked restore, and MinVer configuration.

## Capabilities

### New Capabilities

- `guideline-document-packs`: Provide portable first-party C# and clean-code
  documentation packs sourced from the repository's existing guidance paths.
- `cli-release-automation`: Validate, package, checksum, tag, and release the
  CLI through reusable GitHub Actions workflows.

### Modified Capabilities

- `local-pack-lifecycle`: Accept, validate, persist, and safely apply an
  install destination and same-content adoption request.
- `manifest-schemas`: Represent optional destination metadata in project and
  lock-file records without breaking version-1 manifests that omit it.
- `cli-quality-foundation`: Require reproducible CI build properties and a
  pipeline-compatible version to reach the CLI assembly.

## Impact

- CLI command parsing, installation planning, lifecycle persistence, schemas,
  unit/integration tests, and developer command documentation.
- New packs in `projects/packs/`, installed pack declarations in `lunapack.yml`,
  and resolved target records in `lunapack-lock.yml`.
- Root `Directory.Build.props` and the
  `projects/packs/dotnet-build-props` template; `Directory.Packages.props`,
  `dotnet-tools.json`, and the CLI project dependency declaration.
- New `.github/actions/` and `.github/workflows/` automation plus internal and
  developer documentation. A new ADR records the durable CI and release
  workflow decision.
