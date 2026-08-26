# Create a first pack

Create a small pack, validate it, then install it in a throwaway project.

## Create Pack Content

Create and enter an empty pack directory. Initialize its manifest:

```bash
luna pack init --id documentation-standard --version 1.0.0 --author "Example Engineering" --license MIT
```

Add a file:

```bash
mkdir -p templates
printf '# Engineering standard\n' > templates/standard.md
luna pack add file templates/standard.md --target docs/standard.md
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

## Test Installation

From a separate throwaway project, initialize LunaPack and register the parent
pack directory as a local source:

```bash
luna init
luna sources add local authoring ../packs
luna discover
luna install documentation-standard --dry-run
luna install documentation-standard
```

Check the managed file and lock record. Uninstall only after confirming the
target remains unchanged.
