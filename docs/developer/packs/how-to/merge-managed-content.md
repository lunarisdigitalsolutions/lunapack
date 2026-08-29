# Merge content into shared files

Use a merge strategy when a pack contributes part of a UTF-8 text or JSON file
instead of owning the complete target.

Only merge strategies may share one target. Add the selector with
`--strategy merge:<method>` and test it against representative consumer content.

## Merge unique lines

`merge:lines` keeps target lines in their existing order, then appends source
lines that do not already occur:

```yml
managedFiles:
  - source: templates/gitignore
    target: .gitignore
    strategy: merge:lines
```

Comparison is ordinal and includes whitespace and casing, so `build/`,
`Build/`, and a line beginning with a space followed by `build/` are different
lines. Use this method only when each whole line is an independent entry. It
does not parse comments or key-value formats.

Luna normalizes CRLF and lone CR line endings to LF. The result ends with LF
when it is non-empty and either source or target ended with a newline.

## Replace a marked section

`merge:section` requires source content with at least two lines. The first and
last source lines are exact boundary markers:

```text
# luna:formatting:start
dotnet format --verify-no-changes
# luna:formatting:end
```

```yml
managedFiles:
  - source: templates/formatting.targets
    target: Directory.Build.targets
    strategy: merge
    method: section
```

When neither marker exists, Luna appends the complete source section. When each
marker occurs exactly once in the correct order, Luna replaces that inclusive
range. Installation stops when only one marker exists, either marker is
duplicated, or the closing marker occurs first. Choose pack-specific markers so
another tool or pack cannot create an ambiguous match.

Luna normalizes line endings to LF and retains a trailing newline when either
input had one.

## Merge JSON values

`merge:json` requires valid UTF-8 JSON. Source and target roots must both be
objects or both be arrays:

```yml
managedFiles:
  - source: templates/settings.json
    target: .vscode/settings.json
    strategy: merge
    method: json
```

Luna merges objects recursively, appends array values that are not deeply equal
to an existing value, and uses the source value for scalars, `null`, or nested
values of different kinds. Source `{"editor":{"formatOnSave":true}}`, for
example, preserves unrelated target properties while setting
`editor.formatOnSave` to `true`.

JSON output is rewritten using Luna's serializer, so formatting and property
layout may change.

## Verify lifecycle behavior

```powershell
luna pack validate
luna install engineering-standard --dry-run
```

Test an absent target, expected existing content, ambiguous section markers,
and a user-modified target. Confirm update and uninstall behavior before
release.
