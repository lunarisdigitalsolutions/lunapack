## Why

LunaPack needs a working, small end-to-end slice that proves a project can declare a local pack source and safely adopt or remove a versioned pack. This establishes executable CLI, manifest, schema, pack, test, and quality conventions before lifecycle features expand.

## What Changes

- Add a .NET 10 `lunapack` CLI under `projects/cli/src/` with `init`, `source add`, `install`, and `uninstall` commands.
- Establish `lunapack.yml` as the canonical project manifest and define JSON Schemas under `projects/schema/` for the initial project configuration and pack manifest files.
- Support only a local filesystem source in this slice; `source add local <path>` records it in `lunapack.yml`.
- Add a versioned local `dotnet-gitignore` sample pack under `projects/packs/` that installs and removes its managed `.gitignore` content for a .NET project.
- Add TUnit unit and CLI integration-test foundations, with scenario-based test naming and coverage for every CLI behavior in scope.
- Add C# formatting and analyzer configuration using CSharpier and Roslyn analyzers.
- Document CLI use, local-pack authoring, schemas, testing conventions, C# conventions, and MADR-format internal architectural decisions.

## Capabilities

### New Capabilities

- `cli-project-configuration`: Initialize `lunapack.yml` and add a local source.
- `local-pack-lifecycle`: Install and uninstall a local `dotnet-gitignore` pack.
- `manifest-schemas`: Validate initial LunaPack project and pack manifests.
- `cli-quality-foundation`: Provide the .NET solution, TUnit test structure, C# quality controls, and supporting documentation.

### Modified Capabilities

None.

## Impact

- New .NET 10 solution and CLI projects under `projects/cli/src/`.
- New schema sources under `projects/schema/` and local sample pack content under `projects/packs/`.
- Existing CLI, pack-manifest, MVP, and repository-structure documentation will adopt `lunapack.yml` and the monorepo layout.
- New developer documentation and internal ADRs cover the public contracts, testing, C# standards, and architecture decisions.
- New dependencies include TUnit, CSharpier, and Roslyn analyzer packages; no remote catalog, provider, lock-file, synchronization, validation, or upgrade capability is included.
