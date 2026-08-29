# Release a pack

Prepare and test an immutable pack release before consumers select it.

## Validate the authoring directory

Run these commands from the directory that contains `pack.yml`:

```powershell
luna pack show
luna pack list
luna pack hooks
luna pack validate
```

`luna pack validate` checks the local manifest, local selector matches, and
pack-defined external Git sources. It reports every available issue without
changing the manifest. Fix warnings about unused source aliases unless the
source is intentionally reserved for an imminent selector.

When required metadata is missing, use the authoring commands instead of
rewriting the whole manifest:

```powershell
luna pack set author "Lunaris Digital Solutions <info@lunaris.digital>"
luna pack set license MIT
luna pack set version 1.1.0
luna pack validate
```

## Exercise the release as a consumer

Create an empty fixture project beside the pack catalog. Register the catalog,
then validate the exact release selected through normal discovery:

```powershell
mkdir release-test
cd release-test
luna init
luna sources add local candidate ../packs
luna discover --versions 3
luna validate gitignore-dotnet@1.1.0
luna inspect gitignore-dotnet@1.1.0
```

Unlike `luna pack validate`, `luna validate <pack-reference>` runs from an
initialized consumer project. It resolves the requested release from configured
local sources and validates that selected catalog entry and its local source
files.

Preview before writing files:

```powershell
luna install gitignore-dotnet@1.1.0 --dry-run
luna install gitignore-dotnet@1.1.0
luna audit
```

Supply every meaningful parameter combination. Include both outcomes of each
conditional selector and each managed-file strategy. If the pack declares an
external source, inspect the dry-run source plan and test the approval flow. If
it declares scripts, test `--scripts skip`, then deliberately approve and run
the hooks in a disposable fixture.

## Test update and removal

Install the previous release in another clean fixture, point it at the catalog
that also contains the candidate, and run:

```powershell
luna outdated
luna update gitignore-dotnet --dry-run
luna update gitignore-dotnet
luna audit
luna uninstall gitignore-dotnet
```

Confirm updates preserve user-modified files according to the declared
strategies. Confirm uninstall removes unchanged output but retains changed or
shared output.

## Publish an immutable version

Use Semantic Versioning for consumer-visible behavior. Never replace the
contents of a published version directory. Add a new version directory and keep
older releases available for exact references and update tests. See
[Create a new pack version](../tutorials/create-a-pack-version.md) for a complete
versioned layout.

Commit the release to a local catalog or Git repository only after both
validation commands and the fixture lifecycle pass.
