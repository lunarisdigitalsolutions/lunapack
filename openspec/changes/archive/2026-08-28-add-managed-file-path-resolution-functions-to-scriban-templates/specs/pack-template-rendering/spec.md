## ADDED Requirements

### Requirement: Resolve managed-file targets in templates

LunaPack SHALL expose `files.path(target)` and `files.relative_path(target)` to
managed-file Scriban templates. Each function SHALL look up `target` by a
managed file's manifest-declared target in the resolved installation plan.
`files.path` SHALL return the selected file's effective project-relative target.
`files.relative_path` SHALL return the relative path from the current template
file's effective target directory to the selected file's effective target.
Both functions SHALL use the effective targets after remapping, SHALL return
paths with `/` separators on every platform, and SHALL behave identically while
planning installation, update, and dry-run operations. The functions SHALL
expose no filesystem discovery, reading, writing, or existence checks to the
template.

#### Scenario: Resolve a remapped managed-file target

- **WHEN** a template calls `files.path` with declared target
  `docs/development/code-review.md` and the resolved installation plan remaps
  that file to `docs/04-development/process/code-review.md`
- **THEN** the function returns
  `docs/04-development/process/code-review.md`

#### Scenario: Calculate a relative path from effective targets

- **WHEN** a template calls `files.relative_path` for a selected managed file
  and both files have effective targets changed by remapping
- **THEN** the function returns the `/`-separated lexical relative path from the
  current template file's effective target directory to the referenced file's
  effective target

#### Scenario: Preserve resolution across lifecycle planning modes

- **WHEN** the same resolved graph, parameters, remapping, and lock state are
  planned for installation, update, and dry-run operations
- **THEN** each operation renders the same values from `files.path` and
  `files.relative_path`

### Requirement: Preserve unresolved managed-file references

When either managed-file path function cannot identify exactly one selected
managed file by the supplied declared target, LunaPack SHALL emit a warning that
identifies the unresolved declared target and the current template's effective
target. Rendering SHALL continue, and the function SHALL return the supplied
declared target unchanged. A managed file excluded by its condition SHALL be
unavailable to both functions and SHALL use the same warning and fallback
behavior. These warnings SHALL not make installation, update, or dry-run
planning fail.

#### Scenario: Preserve a missing declared target

- **WHEN** a managed-file template references a declared target absent from the
  resolved installation plan
- **THEN** LunaPack warns that the target could not be resolved while rendering
  the current effective target and renders the original declared target
  unchanged

#### Scenario: Preserve a conditionally excluded target

- **WHEN** a managed-file template references a managed file whose condition
  excludes it from the resolved installation plan
- **THEN** LunaPack emits the unresolved-target warning and renders the original
  declared target unchanged without failing the operation

#### Scenario: Preserve an ambiguous declared target

- **WHEN** more than one selected managed file has the referenced declared
  target
- **THEN** LunaPack treats the reference as unresolved, emits the warning, and
  renders the original declared target unchanged
