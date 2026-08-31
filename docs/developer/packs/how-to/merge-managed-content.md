# Merge content into shared files

Use a merge strategy when a pack contributes part of a UTF-8 text or JSON file
instead of owning the complete target.

Run these authoring commands against a synthetic pack created with
[Add managed content](add-managed-content.md), not a maintained pack under
`projects/packs`.

Only merge strategies may share one target. Add the selector with
`--strategy merge:<method>` and test it against representative consumer content.

Sharing applies across different pack IDs. Two selectors from the same pack
cannot resolve to one target, even when both use merge strategies. Directory or
glob expansion and consumer remapping can create this collision, so verify the
expanded dry-run plan rather than checking only literal targets in `pack.yml`.

## Order contributions

Luna applies contributors in resolved graph order. Dependencies contribute
before consumers, sibling dependencies follow their `packs` declaration order,
and requested roots follow command or project-configuration order. Managed-file
declarations within one pack retain manifest order. Each contribution merges
into the result of the previous contribution.

This fixed order controls appended lines, JSON array values, and section
placement. Consumers cannot override merge order separately. Pack authors who
need a different order must reorder dependencies or avoid sharing an
order-sensitive target.

## Merge unique lines

`merge:lines` keeps target lines in their existing order, then appends source
lines that do not already occur:

```yml
managedFiles:
  - source: fragments/.gitignore/example-ignore.gitignore
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
  - source: fragments/Directory.Build.targets/example-formatting.targets
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
  - source: fragments/settings.json/example-settings.json
    target: .vscode/settings.json
    strategy: merge
    method: json
```

Luna merges objects recursively, appends array values that are not deeply equal
to an existing value, and uses the source value for scalars, `null`, or nested
values of different kinds. Source `{"editor":{"formatOnSave":true}}`, for
example, preserves unrelated target properties while setting
`editor.formatOnSave` to `true`.

JSON output is rewritten with two-space indentation using Luna's serializer, so
property layout may change.

## Verify lifecycle behavior

```powershell
luna pack validate
luna install example-documentation-standard@1.0.0 --dry-run
```

Test an absent target, expected existing content, ambiguous section markers,
and a user-modified target. Confirm update and uninstall behavior before
release.
