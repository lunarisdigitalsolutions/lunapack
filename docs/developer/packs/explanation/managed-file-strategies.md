# Managed-file strategies through install and update

Managed-file strategies decide how LunaPack combines rendered pack content with
a target that already exists. This page explains the resulting repository state
for every copy and merge method, including shared targets and shared transitive
dependencies.

A strategy belongs to one `managedFiles` selector:

```yml
managedFiles:
  - source: templates/.editorconfig
    target: .editorconfig
    strategy: copy:overwrite
```

Omitting `strategy` means `copy:overwrite`.

## Installation decisions

Luna resolves and renders the complete pack graph before changing files. For
each selected target, installation behaves as follows:

| Target before install             | Planned owner                                               | Result                                                                                       |
| --------------------------------- | ----------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| Absent                            | Any copy or merge strategy                                  | Luna creates the target from the rendered pack content. No merge is needed.                  |
| Present and unowned               | Copy or merge strategy, normal install                      | Luna applies the selected method, then records the resulting file and ownership.             |
| Present and unowned               | `--adopt-existing` and bytes exactly match rendered content | Luna leaves the file unchanged and records ownership.                                        |
| Present and unowned               | `--adopt-existing` and bytes differ                         | Installation fails and leaves files and project state unchanged.                             |
| Owned by another pack using copy  | Any strategy                                                | Installation fails because copy ownership is exclusive.                                      |
| Owned by another pack using merge | Merge strategy from a different pack                        | Luna applies contributors in resolved graph order and records every contributor as an owner. |

Use `--dry-run` before installing over an unowned target. `copy:overwrite` can
discard its current content, while merge methods interpret and rewrite it. Use
`--adopt-existing` instead when the existing bytes already equal the rendered
pack output and no content change is intended.

### Install into an absent target

Given this selector:

```yml
managedFiles:
  - source: templates/.gitignore
    target: .gitignore
    strategy: merge:lines
```

and no `.gitignore` in the repository, installing writes the rendered source
without modification:

```text
# templates/.gitignore and resulting .gitignore
bin/
obj/
```

### Apply a strategy to an unowned target

If the repository already contains:

```text
# .gitignore before install
.env
```

then `copy:overwrite` replaces `.gitignore` with the rendered pack source. A
`merge:lines` selector instead retains `.env` and appends missing source lines.
The other methods behave as described below.

If the existing file exactly matches the rendered source, this command records
ownership without rewriting the file:

```powershell
luna install example-dotnet-ignore --adopt-existing
```

## Update decisions

Update first compares the newly rendered pack content with the source-content
digest stored by the previous installation.

1. If rendered pack content did not change, Luna plans no file action. A local
   edit remains in place and `luna audit` reports the drift.
2. If rendered pack content changed, Luna applies the declared strategy to the
   file currently in the repository. This also applies when that file has local
   edits.
3. If the new release no longer declares the target, update deletes the target
   and removes its ownership record. Use `--dry-run` to review removals.

For example, suppose version `1.0.0` installed `setting=pack-v1`, but a developer
changed the repository file to `setting=local`.

- Updating to a release whose rendered source is still `setting=pack-v1` leaves
  `setting=local` untouched.
- Updating to a release whose rendered source is `setting=pack-v2` invokes the
  configured strategy against `setting=local`.

## Copy methods

Copy strategies treat the target as one complete file with one owner.

### `copy:overwrite`

On update, Luna replaces the current target with newly rendered content.

```text
Before update                 Pack version 2                  After update
--------------                --------------                  ------------
setting=local                 setting=pack-v2                 setting=pack-v2
```

Local changes are discarded when desired pack content changed. Preview the
operation before applying it:

```powershell
luna update example-settings --dry-run
```

### `copy:fail-if-exists`

When changed pack content requires an update and the target exists, the update
fails. The target and project state remain unchanged.

```text
Before update                 Pack version 2                  After failed update
--------------                --------------                  -------------------
setting=local                 setting=pack-v2                 setting=local
```

This method is useful when replacement must always require manual review.

### `copy:skip-if-exists`

When the target exists, Luna keeps its current bytes and records those bytes as
the resulting managed state.

```text
Before update                 Pack version 2                  After update
--------------                --------------                  ------------
setting=local                 setting=pack-v2                 setting=local
```

Use this when the pack should create a default file but stop supplying its
contents after a consumer has one.

### `copy:backup-and-overwrite`

Luna moves the current target to the first unused numeric sibling and writes the
new rendered content.

```text
Before update
  settings.ini       setting=local
  settings.ini.1     older backup

After update
  settings.ini       setting=pack-v2
  settings.ini.1     older backup
  settings.ini.2     setting=local
```

Backup files are unowned. Luna does not update, audit, uninstall, or expire
them.

## Merge methods

Merge strategies let different pack IDs contribute to one target. Contributors
run in resolved graph order, and each contribution receives the previous
contributor's result. Two selectors from the same pack cannot share one target.

### `merge:lines`

Luna keeps existing lines in order and appends source lines not already present.
Comparison is ordinal, including case and whitespace.

```text
Target before                 Pack source                     Result
-------------                 -----------                     ------
bin/                          obj/                            bin/
node_modules/                 bin/                            node_modules/
                                                               obj/
```

`bin/` and `Bin/` are distinct, as is a line containing `bin/` with a leading
space. Line endings become LF. A non-empty result has a trailing LF when either
input had one.

### `merge:section`

The first and last source lines are exact markers. If neither marker exists,
Luna appends the complete section:

```text
Target before                 Pack source                     Result
-------------                 -----------                     ------
<Project />                   <!-- luna:start -->             <Project />
                              <PropertyGroup />               <!-- luna:start -->
                              <!-- luna:end -->               <PropertyGroup />
                                                               <!-- luna:end -->
```

If each marker occurs once in the correct order, Luna replaces the inclusive
marked range:

```text
Before update                 Pack version 2                  After update
-------------                 --------------                  ------------
<!-- luna:start -->           <!-- luna:start -->             <!-- luna:start -->
<Old />                       <New />                         <New />
<!-- luna:end -->             <!-- luna:end -->               <!-- luna:end -->
```

The operation fails when only one marker exists, a marker appears more than
once, or the closing marker appears first. Use pack-specific markers to avoid
collisions.

### `merge:json`

Object roots merge recursively. Source scalar values replace matching target
values, while unrelated target properties remain:

```json
// Target before
{
  "editor": {
    "fontSize": 14,
    "formatOnSave": false
  }
}
```

```json
// Pack source
{
  "editor": {
    "formatOnSave": true
  },
  "files.trimTrailingWhitespace": true
}
```

```json
// Result
{
  "editor": {
    "fontSize": 14,
    "formatOnSave": true
  },
  "files.trimTrailingWhitespace": true
}
```

Array roots retain target order and append source values that are not deeply
equal to existing values:

```text
Target before: ["build", {"name":"test"}]
Pack source:   [{"name":"test"}, "publish"]
Result:        ["build", {"name":"test"}, "publish"]
```

Both roots must be objects or both must be arrays, and both inputs must be valid
UTF-8 JSON. Luna rewrites successful JSON results with two-space indentation.

## Multiple packs targeting one file

Suppose `example-dotnet-ignore` and `example-node-ignore` both contribute to
`.gitignore` with `merge:lines`:

```text
example-dotnet-ignore source  example-node-ignore source
----------------------------  --------------------------
bin/                           node_modules/
obj/                           dist/
```

Installing both produces one file in graph order:

```text
bin/
obj/
node_modules/
dist/
```

Both packs record ownership of the final digest. Updating only
`example-node-ignore` merges its new source into the current shared target while
preserving the other pack's lines. A copy contributor, or two contributors from
the same pack ID, makes the plan invalid before files change.

For section merges, uninstall can remove only the uninstalled pack's marked
section. Line and JSON merge uninstall retains the shared target because Luna
cannot safely infer which remaining lines or values belong exclusively to one
contributor.

## Shared transitive packs

A transitive pack is a dependency selected through another pack. If two roots
reference the same transitive pack at the same exact version, Luna represents it
as one graph node:

```text
example-api ──────┐
                  ├── example-dotnet-baseline@1.0.0
example-worker ───┘
```

Installing both roots writes `example-dotnet-baseline` managed files once and
stores one resolved dependency record. The dependency is not applied twice and
is not treated as two packs competing for its own targets.

On update:

- If both roots still resolve `example-dotnet-baseline@1.0.0`, its unchanged
  files are not applied a second time.
- If the selected graph moves the shared node to `2.0.0`, its changed files are
  updated once according to their strategies.
- If roots require conflicting exact versions of the same pack ID, graph
  resolution fails before files or state change.
- If an update removes one incoming reference, the shared dependency and its
  files remain while another installed root can still reach it.
- The dependency becomes removable only after no installed root reaches it.

This shared-node behavior differs from two independent packs intentionally
contributing to one target. A shared dependency owns its managed files once;
merge sharing records several distinct pack owners for one target.

## Preview and verify

Preview the complete graph and file actions before installation or update:

```powershell
luna install example-documentation-standard@1.0.0 --dry-run
luna update example-documentation-standard --dry-run
```

After mutation, inspect ownership and local drift:

```powershell
luna audit
```

See [Add managed content](../how-to/add-managed-content.md) for authoring syntax,
[Merge content into shared files](../how-to/merge-managed-content.md) for merge
constraints, and [Ownership and safety](ownership-and-safety.md) for uninstall
and recovery behavior.
