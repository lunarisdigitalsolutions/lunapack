## 1. Five-Target Build Artifacts

- [x] 1.1 Extend the CLI runtime build configuration and release target map to `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`, using matching native runners and rejecting other manual targets.
- [x] 1.2 Update the CLI build and release composite actions to create, upload, validate, checksum, and attach the exact five versioned ZIP or `tar.gz` archives.
- [x] 1.3 Add focused checks for target selection and release archive validation, including unsupported target and missing or unexpected archive cases.

## 2. Distribution Package Staging

- [x] 2.1 Create distribution templates and a single target mapping outside `Lunapack.Cli.csproj` that stages the five validated native publish outputs for npm packaging.
- [x] 2.2 Create the five constrained npm binary packages and the `@lunarisdigitalsolutions/lunapack` entry package, including exact optional dependencies and a `luna` launcher that resolves, executes, and diagnoses platform payloads.
- [x] 2.3 Add tests for npm metadata, package contents, supported-host selection, missing optional dependency handling, unsupported platform diagnostics, and argument or exit-code forwarding.
- [x] 2.4 Create a .NET SDK RID-specific `Lunaris.Lunapack.Luna` tool package definition that keeps package metadata outside `Lunapack.Cli.csproj` and maps each supported host to its self-contained CLI package.
- [x] 2.5 Add tests that inspect the generated NuGet pointer and RID package layout, platform selection, and RID-first publication order.

## 3. Ordered Registry Publishing

- [x] 3.1 Extend the CLI release composite action to extract validated archives after GitHub Release creation and generate npm inputs plus .NET RID-specific tool packages with the release tag version.
- [x] 3.2 Configure npm trusted publishing for the five platform packages before the entry package, using provenance and `latest` for stable releases or `next` for prereleases.
- [x] 3.3 Configure NuGet trusted publishing of the RID-specific `Lunaris.Lunapack.Luna` tool packages before the pointer package through a temporary OIDC-issued key after npm publication.
- [x] 3.4 Implement and verify failure reporting and rerun recovery that detect completed immutable package versions without treating an incomplete release as successful.

## 4. Documentation And Architecture Records

- [x] 4.1 Create ADR-0038 from the repository template to adopt multi-platform, multi-channel CLI distribution and update the ADR index.
- [x] 4.2 Update developer installation guidance with GitHub Release, npm, and `dotnet tool` installation and verification instructions for all supported platforms.
- [x] 4.3 Update internal release guidance with artifact names, registry prerequisites, required secrets, publication order, and partial-publication recovery.
- [x] 4.4 Update the CLI product requirement and `CHANGELOG.md` with the new consumer-visible distribution channels and platform availability.

## 5. End-to-End Validation

- [x] 5.1 Run locked restore, solution tests, and Release native publishes for all five RIDs on Ubuntu-compatible tooling; confirm every output has the expected executable name and archive format.
- [x] 5.2 Dry-run npm packing and NuGet packing from staged release artifacts; inspect package metadata, contents, versions, and platform restrictions without publishing to external registries.
- [x] 5.3 Validate the release workflow and composite-action syntax, package publish ordering, secret references, Markdown links, and OpenSpec change artifacts.
