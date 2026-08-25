## Context

See [proposal.md](proposal.md) for motivation. The repository currently has no .NET implementation, schemas, packs, or runtime catalog. Existing planning documentation names `lunapack.yaml`, `schemas/`, and `pack.yml`; this first implementation explicitly establishes `lunapack.yml` and `projects/schema/` while retaining `pack.yml` for pack manifests.

The broader provider contract, dependency resolution, and lock file remain planned architecture. This slice needs local content discovery and safe file ownership only.

## Goals / Non-Goals

**Goals:**

- Build the smallest independently testable CLI path from initialization through local-pack installation and removal.
- Make manifest and pack structures schema-defined, versioned, and shared by runtime validation and tests.
- Preserve user-owned content when installation or removal is unsafe.
- Establish durable .NET, testing, formatting, analyzer, and documentation conventions.

**Non-Goals:**

- Lock files, dependency resolution, pack dependencies, version selection, catalog search, remote providers, trust, sync, diff, validation, upgrades, or package removal aliases.
- Arbitrary file mutation or support for pack content beyond the sample managed `.gitignore`.
- Compatibility with `lunapack.yaml`; no runtime exists to migrate, and `lunapack.yml` is the initial canonical name.

## Decisions

### Keep the first solution small and layered by responsibility

Place `Lunapack.sln`, the `Lunapack.Cli` executable project, and separate TUnit unit and integration test projects under `projects/cli/src/`. The executable owns command-line parsing and exit reporting. Small application services own manifest, source, install, and uninstall use cases. Filesystem, YAML serialization, schema validation, hashing, and local pack discovery sit behind interfaces so their behavior can be unit tested without process or disk dependencies.

This avoids introducing a catalog or provider subsystem before it has a real second implementation while preserving a replaceable local-source boundary.

Alternatives considered:

- Multiple production projects now: rejected because one local source and one pack do not justify extra project boundaries.
- Put tests beside production code: rejected because test execution and ownership should remain visibly separate.

### Define one explicit local-pack layout

`projects/packs/` is a local source root. A pack resides at `projects/packs/<pack-id>/`, contains `pack.yml`, and declares its semantic version and managed-file mappings. The initial source contains `dotnet-gitignore` version `1.0.0`, with a template mapped to `.gitignore`.

`lunapack.yml` stores schema version `1`, configured local sources, and installed-pack records. An installed record includes the pack ID, resolved manifest version, source path, target path, and SHA-256 content digest. This is lifecycle state for this narrow feature, not a lock file or general resolver result.

Alternatives considered:

- A version-directory catalog or a lock file: rejected because neither version selection nor reproducible multi-pack resolution is in scope.
- Infer installed files from disk during uninstall: rejected because the manifest must identify files LunaPack owns and protect user changes.

### Treat JSON Schemas as the manifest contracts

Publish `lunapack.schema.json` and `pack.schema.json` under `projects/schema/`. Both schemas use an explicit versioned contract and disallow unsupported source types or incomplete managed-file definitions. YAML input is deserialized to an in-memory representation and validated against the corresponding JSON Schema before lifecycle mutation.

Implementation uses maintained YAML and JSON Schema libraries selected at implementation time, with versions centralized in the .NET dependency configuration. Schema tests validate valid repository samples and invalid boundary documents.

Alternatives considered:

- Hand-written validation only: rejected because consumers and pack authors need portable, machine-readable contracts.
- YAML Schema: rejected because JSON Schema provides mature .NET validation support and applies to the YAML data model.

### Use conservative file ownership rules

Install rejects an existing target unless the same pack already owns it, and duplicate installation is rejected. Installation creates the target before recording its digest and rolls the target back if updating `lunapack.yml` fails. Uninstall verifies the recorded SHA-256 digest before removal; a changed or missing target causes a non-success result and leaves lifecycle state unchanged. Manifest writes use a temporary sibling file followed by replacement to avoid partial YAML.

Alternatives considered:

- Overwrite or force flags: rejected because they expand mutation semantics beyond the requested safe lifecycle.
- Always delete on uninstall: rejected because it can remove user edits.

### Test through both boundaries

TUnit unit tests exercise command parsing, validation, local discovery, YAML mapping, digest comparison, and each success or failure path via isolated abstractions. Integration tests start the built `Lunapack.Cli` assembly in unique temporary directories and execute `init`, `source add local`, `install dotnet-gitignore`, and `uninstall dotnet-gitignore` against `projects/packs/`.

Test classes and methods use `Scenario_Condition_ExpectedOutcome`. Test sources contain no Arrange-Act-Assert narration comments. The test projects are executed with the solution's standard `dotnet test` command.

Alternatives considered:

- Unit tests only: rejected because command parsing, process exit behavior, working-directory handling, and file integration need executable proof.
- Integration tests via `dotnet run`: rejected because tests should invoke the built artifact and avoid restore/build work per test.

### Make quality checks repository conventions

Use the latest supported C# language version on .NET 10. Root build configuration enables .NET/Roslyn analyzers, treats analyzer warnings as errors, and adds a maintained supplementary Roslyn analyzer package. CSharpier configuration supplies formatting; documented commands format-check and build/test the solution. The codebase uses concise internal C# conventions focused on naming, nullability, immutability, cancellation, async I/O, and test naming rather than a large style guide.

Alternatives considered:

- Formatting only: rejected because formatting does not find API, correctness, or maintainability issues.
- Style-only analyzers: rejected because compiler and .NET analyzers provide the needed baseline with less prescriptive noise.

## Risks / Trade-offs

- Relative local source paths can stop resolving after a project move -> resolve paths relative to the manifest directory and report an actionable error when unavailable.
- Manifest state is not a reproducible dependency lock -> limit records to the installed sample pack and add lock behavior only in a later lifecycle change.
- A single `.gitignore` target has no merge strategy -> reject existing files and preserve modified managed files.
- Tool package versions can change across implementation time -> centralize versions and pin them in the solution configuration.

## Migration Plan

1. Add the monorepo project roots, schemas, local sample pack, and .NET solution.
2. Implement and test the CLI behavior against the version `1` schemas.
3. Update product, developer, and internal documentation to name `lunapack.yml` and `projects/schema/` as the initial conventions.
4. Add MADR-based ADRs for the manifest/location decision, safe local file ownership, and the TUnit/C# quality baseline.

No deployment migration or compatibility alias is required because no prior CLI or manifest files exist. Reverting this change removes the new project roots and documentation; created consumer-project files are outside this repository and remain under user control.
