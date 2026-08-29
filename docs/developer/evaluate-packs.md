# Evaluate a pack before installation

Find a pack, inspect its exact release, and preview its changes before allowing
Luna to write project files.

## Find available packs

List the preferred latest release of each pack ID across configured sources:

```powershell
luna discover
```

Search IDs, descriptions, and tags when you know the capability rather than the
pack ID:

```powershell
luna search guidelines
```

Add `--versions 3` to either command when you need to compare older releases.
The value can be from 1 through 10; results use descending Semantic Version
order.

## Inspect an exact release

Pin the version while reviewing identity, attribution, parameters, and
dependencies:

```powershell
luna inspect clean-code-guidelines@1.0.0
```

Confirm the author, license, managed targets, required parameters, and referenced
packs match what you intend to add. Review configured identities separately with
`luna sources list`; pack IDs do not prove publisher identity.

`luna validate <id>@<version>` provides additional manifest and selected-file
validation for packs in configured local sources.

## Preview project changes

Run installation as a dry run with scripts disabled:

```powershell
luna install clean-code-guidelines@1.0.0 `
  --dry-run `
  --scripts skip
```

Review every target action and proposed external source. A successful dry run
does not write managed files or LunaPack state. It also does not guarantee that
an approved lifecycle script is safe; review scripts separately before changing
the mode.

Continue with [Discover and install packs](install-packs.md) when the exact
release and preview are acceptable. See [Security model and risks](threat-model.md)
for source, script, and managed-file boundaries.
