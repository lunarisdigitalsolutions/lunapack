# Create a first pack

Create a small pack, validate it, then install it in a throwaway project.

## Create Pack Content

Create a versioned local catalog and enter an empty pack directory:

```bash
mkdir -p engineering-packs/packs/example-documentation-standard/1.0.0
cd engineering-packs/packs/example-documentation-standard/1.0.0
luna pack init --id example-documentation-standard --version 1.0.0 --author "Example Engineering" --license MIT
```

Add a file:

```bash
mkdir -p targets/docs
printf '# Engineering standard\n' > targets/docs/standard.md
luna pack add file targets/docs/standard.md --target docs/standard.md
```

Add optional metadata:

```bash
luna pack set name "Documentation standard"
luna pack set description "Adds the engineering documentation standard."
```

Inspect and validate the result:

```bash
luna pack show
luna pack list
luna pack validate
```

The manifest remains normal YAML. Direct edits are supported, but rerun
`luna pack validate` before distributing the pack.

When your configured catalog contains `lunapack-pack-authoring`, you can instead
install its minimal scaffold into a new release directory:

```powershell
luna install lunapack-pack-authoring@1.0.0 `
  --destination packs/example-documentation-standard/1.0.0
```

Replace the generated example identity, attribution, description, target, and
content before validation.

## Test Installation

Return to `engineering-packs`, create a separate throwaway project, then
initialize LunaPack and register the catalog:

```bash
mkdir release-test
cd release-test
luna init
luna sources add local authoring ../packs
luna discover
luna install example-documentation-standard@1.0.0 --dry-run
luna install example-documentation-standard@1.0.0
```

Check the managed file and lock record. Uninstall only after confirming the
target remains unchanged.
