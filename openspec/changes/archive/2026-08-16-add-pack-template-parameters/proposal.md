## Why

Pack authors cannot tailor managed-file content or select files from a pack at
install time. Consumers must currently edit copied files manually, making
reusable policy packs less useful and preventing a repository from providing
its own standard license as a managed pack.

## What Changes

- Add typed, required or optional parameter declarations to `pack.yml`,
  including constrained enum values.
- Render every selected managed-file template with Scriban before ownership
  checks, copying, and digest persistence; enable parameter-based file
  conditions.
- Add repeatable `lunapack install --parameter` (`-p`) input, strict value
  validation, required-parameter failures, and variable-precedence controls.
- Add project-level `variables` to `lunapack.yml`, automatic matching-variable
  binding, and global or per-name variable skipping during installation.
- Aggregate one compatible parameter set across a resolved composite pack
  graph before any installation mutation.
- Publish and install a `license-mit` bundled pack whose template uses the
  configured company name and Scriban's current year.

## Capabilities

### New Capabilities

- `pack-template-rendering`: Render parameterized managed-file templates and
  conditionally materialize their files during pack installation.

### Modified Capabilities

- `manifest-schemas`: Validate pack parameter declarations, managed-file
  conditions, and project-level variables.
- `cli-project-configuration`: Initialize, validate, and preserve project
  variables alongside existing portable configuration.
- `local-pack-lifecycle`: Collect inputs, resolve composite-graph parameters,
  render templates, and retain safe transactional ownership behavior.
- `bundled-engineering-packs`: Publish and dogfood the parameterized MIT
  license pack in this repository.

## Impact

- CLI: installation option parsing, parameter resolution, graph planning,
  rendering, copy/adoption digest checks, and error reporting.
- Models and schemas: `PackManifest`, managed-file declarations, project
  configuration, and their JSON Schema fixtures and tests.
- Dependency: add Scriban to the CLI's centrally managed .NET dependencies.
- Packs and state: add the `license-mit` source pack, configure the Lunaris
  Digital Solutions company variable, and record the repository installation.
- Documentation: update product requirements, internal lifecycle and pack
  architecture guidance, developer configuration, manifest, and install
  references; add an ADR for the durable parameter-resolution and rendering
  contract.
