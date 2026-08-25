## Why

LunaPack currently proves local lifecycle behavior with one `dotnet-gitignore`
sample, but it does not distribute the engineering conventions used to build
LunaPack itself. Bundling small, independently useful packs makes the repository
its first catalog producer and provides realistic content for discovery,
installation, ownership, and removal workflows.

## What Changes

- Add bundled packs for a .NET Git ignore file, pinned .NET SDK,
  EditorConfig conventions, CSharpier local-tool manifest, shared .NET quality
  baseline, and a MADR ADR template.
- Configure this repository as a LunaPack consumer with `projects/packs` as a
  local source and install the bundled packs into the repository root.
- Restore repository-specific content manually after installation where the
  generic pack intentionally omits it.
- Document the bundled-pack catalog, prerequisites, target paths, and the
  version-1 restriction that packs copy complete files only and cannot
  overwrite, merge, or create missing parent directories.
- Add focused catalog and lifecycle coverage for the bundled packs and the
  repository consumer configuration.

## Capabilities

### New Capabilities

- `bundled-engineering-packs`: Publish small, reusable engineering convention
  packs from the repository local source and configure the repository to
  install them as a consumer.

### Modified Capabilities

- `local-pack-lifecycle`: Extend the bundled-pack contract beyond
  `dotnet-gitignore` while retaining conservative install and uninstall
  ownership guarantees.

## Impact

- Affected content: `projects/packs/`, root `lunapack.yml`, repository and pack
  documentation, and local-source catalog and lifecycle tests.
- Affected contracts: the existing version-1 `pack.yml` and `lunapack.yml`
  schemas are reused without schema or CLI changes.
- Affected documentation: product milestone material, internal pack and
  lifecycle architecture guidance, and developer pack-author and pack-consumer
  guidance.
