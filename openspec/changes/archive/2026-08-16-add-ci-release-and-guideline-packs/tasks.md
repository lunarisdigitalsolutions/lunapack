## 1. Destination-Aware Pack Lifecycle

- [x] 1.1 Add optional `destination` state to direct requested-pack and resolved
      pack models, `lunapack.yml` and `lunapack-lock.yml` schemas, and schema fixtures;
      preserve validation of existing version-1 state that omits the field.
- [x] 1.2 Add the `lunapack install` `--destination` and `--adopt-existing`
      options, pass a validated installation request through the command boundary,
      and reject empty, absolute, and project-escaping destinations before state or
      file mutation.
- [x] 1.3 Update installation planning and lock-state creation so a destination
      relocates only direct-pack managed targets, persists in both state files, and
      leaves dependency-managed targets unchanged.
- [x] 1.4 Implement explicit digest-matched adoption as part of the existing
      copy-and-save transaction; preserve atomic rollback and unowned-target
      protection for every mismatch or omitted adoption option.
- [x] 1.5 Add focused unit, integration, and schema tests for default and
      destination installs, unsafe destination rejection, dependency target
      preservation, matching adoption, mismatched adoption rollback, state
      persistence, and destination-aware uninstall.

## 2. Portable Guideline Packs

- [x] 2.1 Revise
      `docs/internal/development/coding-guidelines/csharp.md` and
      `docs/internal/development/coding-guidelines/clean-code.md` into portable
      guidance, removing or generalizing LunaPack-specific operational content without
      duplicating their prior text in planning artifacts.
- [x] 2.2 Create `csharp-guidelines` and `clean-code-guidelines` under
      `projects/packs/`, each with a schema-valid `pack.yml` and one Markdown
      template byte-identical to its portable source document.
- [x] 2.3 Add both packs to the repository's `lunapack.yml`, then install them at
      `docs/internal/development/coding-guidelines` using the new destination and
      explicit adoption behavior; verify `lunapack-lock.yml` records destinations,
      effective targets, and SHA-256 digests.
- [x] 2.4 Update `docs/developer/cli/commands/install.md` and
      `docs/developer/packs/pack-manifest.md` with destination, adoption,
      ownership-protection, state-persistence, and uninstall behavior.

## 3. Deterministic Build Policy

- [x] 3.1 Apply the requested CI-only deterministic, Source Link, and
      `ContinuousIntegrationBuild` properties plus informational-version and
      lock-file restore policy and `MinVerDefaultPreReleaseIdentifiers` set to
      `preview` to both root `Directory.Build.props` and the
      `dotnet-build-props` template.
- [x] 3.2 Add the privately consumed `MinVer` build package through central
      package management and the CLI project, and register `minver-cli` in
      `dotnet-tools.json` for repository-local restore.
- [x] 3.3 Verify Source Link and MinVer with the pinned .NET SDK: run
      `dotnet tool restore`, capture the default `minver` standard output, pass it
      as `MINVERVERSIONOVERRIDE`, and verify consistent assembly, file,
      informational, package, and semantic version metadata. Add a centrally
      versioned GitHub Source Link provider only when the build proves it necessary.

## 4. Reusable CI And Release Automation

- [x] 4.1 Create `.github/actions/build-dotnet/action.yml` with generic
      solution, project, test, coverage, optional publish, artifact, and
      additional-publish-argument input; restore in locked mode, build, and
      test the supplied solution.
- [x] 4.2 Create `.github/actions/cli/build/action.yml` and
      `.github/actions/cli/release/action.yml`. The build action composes the
      generic action, restores local tools, resolves the MinVer version,
      creates host-specific ZIP or tar.gz archives, and uploads RID-qualified
      artifacts; the release action creates checksums and stages the changelog.
- [x] 4.3 Create `.github/workflows/cli.yml` with a tag-push trigger, full Git
      history and tag checkout, a `releases/*` tag filter, and default
      Windows/Linux x86/x64 matrix rows. Add a workflow-dispatch
      comma-separated runtime-identifier input; use the pushed tag directly
      for GitHub Release versioning and its semantic suffix for tag build and
      archive versioning.
- [x] 4.4 Implement the `release` job on Ubuntu with minimum `contents: write`
      permission. It invokes the release action to download all distribution
      artifacts, generate a sorted SHA-256 manifest, stage root `CHANGELOG.md`,
      and publish the existing tag, archives, checksum file, and changelog.
- [x] 4.5 Create `.github/workflows/cli-pr.yml` for pull requests that invokes
      the same CLI build action in Linux x64-only mode with artifact publication
      disabled and no release or repository-write permission.
- [x] 4.6 Validate workflow syntax, composite-action inputs, matrix filtering,
      artifact paths, default hostless Linux x86 publication, tag version
      propagation, changelog staging, and PR artifact/release exclusion with
      repository-supported static checks and a controlled tagged-release dry run
      where GitHub permissions permit it.

## 5. Documentation And Verification

- [x] 5.1 Record the lock-validation boundary now retained in ADR-0023, update
      the ADR index, and document release tag versioning, manual build selection,
      changelog assets, and checksum verification responsibilities.
- [x] 5.2 Add root `CHANGELOG.md` with version, notable-change, and commit
      sections consumed by the tag-release workflow.
- [x] 5.3 Run `dotnet tool restore`, CSharpier verification, the CLI solution
      build, the complete TUnit suite, schema tests, documentation linting,
      Markdown-link validation, OpenSpec strict validation, and the focused
      workflow checks; resolve only failures introduced by this change.
