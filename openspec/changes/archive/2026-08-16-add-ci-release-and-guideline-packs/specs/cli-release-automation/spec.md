## Purpose

Build, verify, package, checksum, and release the LunaPack CLI consistently
through composable GitHub Actions with platform-appropriate runners.

## ADDED Requirements

### Requirement: Reuse CLI build automation

The repository SHALL provide a generic .NET build composite action, a CLI build
composite action, and a CLI release composite action. The tag workflow and the
pull-request workflow SHALL invoke the same CLI build action. The CLI build
action SHALL invoke the generic action for SDK setup, restore, build, test,
coverage, optional publish, artifact upload, and published-file reporting.

#### Scenario: Reuse the build workflow from main CI

- **WHEN** the main CI/release workflow runs
- **THEN** it invokes the CLI build composite action rather than duplicating
  the CLI build steps

#### Scenario: Reuse the build workflow from pull-request CI

- **WHEN** the pull-request workflow runs
- **THEN** it invokes the same CLI build composite action with artifact
  publication disabled

### Requirement: Build and archive supported CLI distributions

The `releases/*` tag-triggered release workflow SHALL publish CLI distributions
for Windows and Linux x86/x64 by default. Manual workflow dispatch SHALL accept
a comma-separated list of supported runtime identifiers and SHALL build only
the selected targets. Windows distributions SHALL be ZIP archives; Linux and
macOS distributions SHALL be tar.gz archives. Each distribution archive SHALL
be uploaded as a distinct build artifact for the release action.

Windows targets SHALL build on Windows hosted runners, Linux targets on Ubuntu
hosted runners, and macOS targets on macOS hosted runners. macOS targets SHALL
be disabled by default. macOS x86 is unsupported. Additional publish arguments
SHALL be a whitespace-delimited string so callers can opt into self-contained
or AOT output. Linux x86 SHALL use hostless framework-dependent output and
reject AOT, self-contained, or application-host arguments because the pinned
SDK provides neither an application host nor Native AOT runtime pack for that
RID.

#### Scenario: Build default tag targets

- **WHEN** a `releases/*` tag is pushed
- **THEN** it publishes Windows and Linux x86/x64 archives and does not
  schedule ARM or macOS builds

#### Scenario: Build manual targets on request

- **WHEN** a manual run supplies `osx-x64,osx-arm64`
- **THEN** it adds macOS x64 and ARM64 build rows without creating a release

### Requirement: Validate every production build

Every release build matrix entry SHALL restore packages in locked mode, build
the CLI solution, run unit and integration tests, and produce code coverage.
The release workflow SHALL publish test results and coverage reports as
separate artifacts from distributable CLI archives.

#### Scenario: Publish validation evidence

- **WHEN** a supported production build completes
- **THEN** its distribution, test-result, and coverage artifacts are all
  available independently

### Requirement: Run a non-releasing pull-request validation build

The pull-request workflow SHALL run for pull requests and build only the Linux
x64 target. It SHALL not publish build, test-result, coverage, or distribution
artifacts, and it SHALL not create a Git tag, GitHub Release, or release asset.

#### Scenario: Validate a pull request

- **WHEN** a pull request triggers CI
- **THEN** the Linux x64 build and validation complete without artifact
  publication or release actions

### Requirement: Release tag artifacts with the pushed tag version

The release workflow SHALL run only when a `releases/<semantic-version>` tag is
pushed. It SHALL pass the semantic suffix to release builds and archive names,
and the full tag to GitHub Release creation. It SHALL not compare the tag with
MinVer. Manual builds SHALL leave the version override empty so MinVer remains
their default version source, and SHALL not create a GitHub Release.

For a successful tagged build, the release job SHALL download every distribution
archive, generate and publish a SHA-256 checksum manifest, and create a GitHub
Release for the existing triggering tag. It SHALL include all archives, the
checksum manifest, and the repository root's `CHANGELOG.md` document. The
release job SHALL not run for pull-request builds.

#### Scenario: Release a tagged build

- **WHEN** a tag is pushed and all required release build targets succeed
- **THEN** GitHub contains a release for that existing tag with every archive,
  one SHA-256 checksum manifest, and the root changelog attached

#### Scenario: Skip release for manual build

- **WHEN** workflow dispatch starts a selected build matrix
- **THEN** it publishes its build artifacts without creating a GitHub Release
