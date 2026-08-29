# Compose packs

Create a pack that installs exact releases of other packs as one resolved
graph.

## Add references

Initialize the root pack, then add dependencies by ID and exact Semantic
Version:

```powershell
luna pack init `
  --id example-dotnet-foundation `
  --author "Lunaris Digital Solutions <info@lunaris.digital>" `
  --license MIT
luna pack add reference dotnet-gitignore 1.0.0
luna pack add reference dotnet-editorconfig 1.0.0
```

Use `--replace` to replace an existing reference added through `pack add`, or
use `luna pack set reference` to create or replace it.

## Bind dependency parameters

Repeat `--parameter` to supply string or Boolean values to a dependency:

```powershell
luna pack add reference github-actions-pr-gate 1.0.0 `
  --parameter required=true `
  --parameter checkName=build
```

A binding remains hidden from consumers unless the root pack declares the same
parameter. Declare it on the root when consumers should choose the value:

```powershell
luna pack set parameter checkName string `
  --required `
  --display-name "Required check name"
```

Every declaration of the same parameter in the graph must use a compatible
type.

## Suppress dependency hooks

Disable selected lifecycle hooks for a transient dependency when the root pack
already performs equivalent work:

```powershell
luna pack add reference dotnet-csharpier-tool 1.0.0 `
  --disable-hook postInstall
```

Suppression applies through the composite reference. Installing that dependency
directly as a root keeps its own hooks enabled.

## Validate the graph

Local validation checks the root manifest. Test graph resolution from a fixture
whose configured sources contain every exact dependency:

```powershell
luna pack validate
luna validate example-dotnet-foundation@1.0.0
luna inspect example-dotnet-foundation@1.0.0
luna install example-dotnet-foundation@1.0.0 --dry-run
```

The consumer operation rejects missing releases, cycles, incompatible
parameter declarations, and target collisions before writing project files.
