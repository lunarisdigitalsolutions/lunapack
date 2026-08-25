## 1. Publish Bundled Pack Content

- [x] 1.1 Expand `dotnet-gitignore` into the complete reusable .NET ignore
      convention and add a catalog description to its manifest.
- [x] 1.2 Create the `dotnet-sdk-10`, `dotnet-editorconfig`, and
      `dotnet-csharpier-tool` packs with versioned manifests, descriptions, and
      templates derived from the repository's corresponding root files.
- [x] 1.3 Create the `dotnet-quality-baseline` pack with versioned manifest,
      description, and complete `Directory.Build.props` plus a generic
      `Directory.Packages.props` template containing only baseline dependencies.
- [x] 1.4 Create the `madr-adr-template` pack with a versioned manifest,
      description, and template targeting `docs/adr/template.md`.
- [x] 1.5 Add schema-valid root `lunapack.yml` that records `projects/packs` as a
      relative local source and every installed bundled pack.

## 2. Verify Catalog and Lifecycle Behavior

- [x] 2.1 Extend catalog unit coverage to validate and discover every bundled
      pack, including descriptions and installed root consumer configuration.
- [x] 2.2 Extend lifecycle integration coverage for installation and safe
      removal of one single-file pack and the multi-file quality baseline in fresh
      consumer workspaces.
- [x] 2.3 Add lifecycle coverage that installs the MADR template only after
      creating `docs/adr`, and confirms installed root overlays preserve their
      changed managed content.

## 3. Document the Dogfooding Contract

- [x] 3.1 Create ADR-0015 from the repository template to supersede the
      source-only consumer decision with an installed-pack and manual-overlay
      convention; add it to the ADR index.
- [x] 3.2 Update product milestone documentation to describe the bundled
      catalog rather than a single sample pack.
- [x] 3.3 Update internal pack-model and source-provider guidance with the
      installed-consumer boundary, complete-file ownership, manual overlays, and
      MADR target-directory limitation.
- [x] 3.4 Update developer pack and CLI guidance with the bundled-pack catalog,
      target paths, installed-consumer setup, manual overlays, and version-1
      lifecycle limitations.

## 4. Validate the Change

- [x] 4.1 Restore local .NET tools and run CSharpier on changed C# files when
      applicable, then run the focused catalog and lifecycle tests followed by the
      full CLI solution test suite.
- [x] 4.2 Run documentation linting, Markdownlint for changed GitHub guidance
      when applicable, strict OpenSpec validation, and `git diff --check`.
- [x] 4.3 From the repository root, run catalog discovery against `lunapack.yml`
      and confirm all bundled packs are visible with recorded installations and
      intentional repository-specific overlays.
