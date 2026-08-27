# Create a new pack version

Publish a second immutable release of an existing pack and test the upgrade.
This tutorial uses `gitignore-dotnet` version `1.1.0`.

## Organize the catalog

Keep one directory per pack ID and one child directory per released version:

```text
engineering-packs/
  packs/
    gitignore-dotnet/
      1.0.0/
        pack.yml
        templates/
          dotnet.gitignore
      1.1.0/
        pack.yml
        templates/
          dotnet.gitignore
```

LunaPack discovers `pack.yml` recursively, so the grouping folders are a
repository convention rather than manifest syntax. This layout keeps releases
easy to review and prevents a new release from replacing old content.

## Copy the previous release

Copy `1.0.0` to `1.1.0`, then enter the new directory. Update only the new
manifest:

```powershell
Copy-Item -Recurse packs/gitignore-dotnet/1.0.0 `
  packs/gitignore-dotnet/1.1.0
cd packs/gitignore-dotnet/1.1.0
luna pack set version 1.1.0
```

Keep the same `id`. Change the version according to Semantic Versioning:

- Patch for compatible fixes to generated content or metadata.
- Minor for compatible new files, parameters, or behavior.
- Major for incompatible targets, defaults, removals, or lifecycle behavior.

Edit templates and declarations in `1.1.0`. Never edit `1.0.0` after it has
been published.

## Validate the candidate

```powershell
luna pack show
luna pack list
luna pack validate
```

Create a fixture beside `packs`, configure that directory as a source, and
validate catalog selection:

```powershell
mkdir release-test
cd release-test
luna init
luna sources add local candidate ../packs
luna discover --versions 2
luna validate gitignore-dotnet@1.1.0
```

## Test the upgrade

Install the old version, preview the update, and apply it:

```powershell
luna install gitignore-dotnet@1.0.0
luna outdated
luna update gitignore-dotnet --dry-run
luna update gitignore-dotnet
luna audit
```

Repeat with a user modification to each managed strategy whose conflict
behavior matters. Test uninstall after the update. Commit both version
directories so exact references remain reproducible.
