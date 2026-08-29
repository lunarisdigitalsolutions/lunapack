# Add managed content

Choose selectors and conflict strategies for files a pack owns in consumer
projects.

Start with `luna pack init --id <id> --author <author> --license <license>`.
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

### Merge unique lines

`merge:lines` requires UTF-8 text. Luna keeps target lines in their existing
order, then appends source lines that do not already occur. Comparison is
ordinal and includes whitespace and casing, so `build/`, `Build/`, and
a line beginning with a space followed by `build/` are different lines.
Luna normalizes CRLF and lone CR line endings to LF. The result ends with LF
when it is non-empty and either source or target ended with a newline.

```yml
managedFiles:
  - source: templates/gitignore
    target: .gitignore
    strategy: merge:lines
```

Use this method only when each whole line is an independent entry, such as an
ignore pattern. It does not parse comments or key-value formats.

### Replace a marked section

`merge:section` requires UTF-8 source content with at least two lines. The first
and last source lines are exact boundary markers:

```text
# luna:formatting:start
dotnet format --verify-no-changes
# luna:formatting:end
```

```yml
managedFiles:
  - source: templates/formatting.targets
    target: Directory.Build.targets
    strategy: merge:section
```

When neither marker exists, Luna appends the complete source section. When each
marker occurs exactly once in the correct order, Luna replaces that inclusive
range. Installation stops when only one marker exists, either marker is
duplicated, or the closing marker occurs first. Choose pack-specific markers so
another tool or pack cannot create an ambiguous match. As with `merge:lines`,
Luna normalizes line endings to LF and retains a trailing newline when either
input had one.

### Merge JSON values

`merge:json` requires valid UTF-8 JSON. Source and target roots must both be
objects or both be arrays. Luna merges objects recursively, appends array values
that are not deeply equal to an existing value, and uses the source value for
scalars, `null`, or nested values of different kinds.

```yml
managedFiles:
  - source: templates/settings.json
    target: .vscode/settings.json
    strategy: merge:json
```

For example, source `{"editor":{"formatOnSave":true}}` preserves unrelated
target properties while setting `editor.formatOnSave` to `true`. JSON output is
rewritten using Luna's serializer, so formatting and property layout may change.

## Inspect and test

```powershell
luna pack list
luna pack validate
luna install engineering-standard --dry-run
```

Test each strategy against an absent target, expected existing content, and a
user-modified target. Confirm update and uninstall behavior before release.
