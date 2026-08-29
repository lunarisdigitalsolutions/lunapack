# Add managed content

Choose selectors and conflict strategies for files a pack owns in consumer
projects.

Start in an empty pack directory with a synthetic ID that cannot collide with a
maintained repository pack:

```powershell
luna pack init --id example-documentation-standard `
  --author "Example Engineering" `
  --license MIT
```

The generated `pack.yml` contains only required author, ID, license, and version
properties. Subsequent authoring commands load that minimal manifest and add
only requested declarations.

## Select files

Use a file selector for one source and a directory or glob for a set:

```powershell
luna pack add file templates/.editorconfig --target .editorconfig
luna pack add directory templates/docs --target docs/standards
luna pack add glob 'templates/**/*.json' --target config
```

Directory and glob matches retain their paths below the target. Use repeatable
`--exclude` patterns to remove matches:

```powershell
luna pack add directory templates/docs `
  --target docs/standards `
  --exclude '**/drafts/**' `
  --exclude '**/*.internal.md'
```

`--flatten` places every selected file directly below the target. Use it only
when selected file names are unique. File selectors do not accept `--exclude`
or `--flatten`.

## Choose a strategy

Set a strategy as `<type>:<method>`:

```powershell
luna pack add file templates/.editorconfig `
  --target .editorconfig `
  --strategy copy:fail-if-exists
luna pack add file templates/gitignore `
  --target .gitignore `
  --strategy merge:lines
```

Copy methods control conflicts with an existing target:

| Method                 | Behavior                                           |
| ---------------------- | -------------------------------------------------- |
| `overwrite`            | Replace target through LunaPack's ownership rules. |
| `fail-if-exists`       | Stop when target already exists.                   |
| `skip-if-exists`       | Leave existing target unchanged.                   |
| `backup-and-overwrite` | Back up target before replacement.                 |

Merge methods are `lines`, `section`, and `json`. Only merge strategies may
share a target. Prefer `lines` for unique line sets, `section` for
marker-bounded text owned by one pack, and `json` for structured JSON content.
See [Merge content into shared files](merge-managed-content.md) for exact line,
section, JSON, newline, and conflict behavior.

## Inspect and test

```powershell
luna pack list
luna pack validate
```

Then switch to the initialized fixture whose local source contains the pack;
[Create a first pack](../tutorials/first-pack.md) shows that setup.

```powershell
luna install example-documentation-standard@1.0.0 --dry-run
```

Test the selected strategy against an absent target, expected existing content,
and a user-modified target. Confirm update and uninstall behavior before
release.
