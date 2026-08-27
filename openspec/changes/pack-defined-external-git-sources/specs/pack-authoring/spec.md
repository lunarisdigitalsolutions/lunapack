# Pack Authoring Delta Specification

## MODIFIED Requirements

### Requirement: Author managed content

The CLI SHALL let authors add file, directory, and glob selectors, list their canonical manifest entries, and remove an entry by its exact selector value. Add commands SHALL support every property valid for that managed-file selector, including target, strategy, template, condition, pack-local source alias, repeatable exclusion patterns, and flattening. External path input SHALL be normalized as a safe path relative to the selected pack or external source root before persistence. A named source alias SHALL already exist in the same pack manifest. Duplicate or ambiguous selectors, invalid glob syntax, unknown aliases, escaping paths, and flattened selections with duplicate target names SHALL be rejected without changing the manifest.

#### Scenario: Add a file

- **WHEN** an author runs `luna pack add file README.md`
- **THEN** the manifest gains a file selector with source and target `README.md` that resolves from the pack source

#### Scenario: Add an external file

- **WHEN** an author runs `luna pack add file instructions/dotnet.instructions.md --source upstream --target .github/instructions/dotnet.instructions.md` for a declared alias
- **THEN** the manifest gains a source-aware file selector with the requested target

#### Scenario: Add a directory

- **WHEN** an author runs `luna pack add directory templates --source upstream --target templates`
- **THEN** the manifest gains a recursive directory selector that preserves descendant paths below `templates`

#### Scenario: Add a glob

- **WHEN** an author runs `luna pack add glob "docs/**/*.md" --source upstream --exclude "**/README.md" --target docs/standards`
- **THEN** the manifest gains a glob selector using that canonical pattern, exclusion, source alias, and target

#### Scenario: Configure managed-file behavior

- **WHEN** an author adds a selector with target, strategy, template, condition, exclusions, or flattening options
- **THEN** LunaPack persists those values using the published manifest shape

#### Scenario: Remove an exact selector

- **WHEN** an author runs `luna pack rm "docs/**/*.md"` and exactly one selector has that value
- **THEN** LunaPack removes that managed-file entry

#### Scenario: Reject an unknown source alias

- **WHEN** an author adds managed content using a source alias absent from the pack manifest
- **THEN** LunaPack reports the unknown alias, recommends commands to add it, and leaves `pack.yml` unchanged

#### Scenario: Reject an unsafe path

- **WHEN** an add or remove command receives a rooted or escaping source or target path
- **THEN** LunaPack reports the invalid input and leaves `pack.yml` unchanged

### Requirement: Inspect and validate authoring state

`luna pack list` SHALL display managed content, references, and script hooks. `luna pack sources` SHALL display each pack-defined source's alias, sanitized repository identity, canonical ref, normalized path, and reference count. `luna pack scripts` SHALL display script details. `luna pack show` SHALL display identity, metadata, and summary counts. `luna pack validate` SHALL validate the local manifest and report actionable locations for every available violation. Validation SHALL warn without failing for an unused source declaration. When repository access is available, validation SHALL also resolve refs, base paths, selectors, exclusions, and flattened targets; inaccessible resolution SHALL return a non-success result without changing the manifest.

#### Scenario: List pack contents

- **WHEN** an author runs `luna pack list`
- **THEN** LunaPack displays canonical managed selectors, composite references, and lifecycle hook names without mutating the manifest

#### Scenario: List external sources

- **WHEN** an author runs `luna pack sources` for a pack with external sources
- **THEN** LunaPack displays each alias with sanitized identity, canonical ref, normalized path, and managed-file reference count

#### Scenario: Show a pack summary

- **WHEN** an author runs `luna pack show`
- **THEN** LunaPack displays the pack identity, version, and counts for managed files, external sources, scripts, references, parameters, and tags

#### Scenario: Validate a valid local manifest

- **WHEN** an author runs `luna pack validate` for a valid `pack.yml`
- **THEN** LunaPack reports success and exits successfully

#### Scenario: Warn about an unused external source

- **WHEN** a valid pack declares an external source that no managed file references
- **THEN** validation succeeds with a warning naming the unused alias

#### Scenario: Validate an invalid local manifest

- **WHEN** an author runs `luna pack validate` for an invalid `pack.yml`
- **THEN** LunaPack reports every available schema and resolution violation with its manifest location and exits unsuccessfully

## ADDED Requirements

### Requirement: Author pack-defined Git sources

`luna pack add source git <name> <repository-url> --ref <ref>` and `luna pack add source github <name> <owner/repository> --ref <ref>` SHALL add one pack-local Git source alias to the selected manifest. Both commands SHALL accept optional `--path`, `--description`, `--manifest`, and `--workspace` options, require an explicit source identifier and ref, canonicalize the ref before writing, and reject duplicate aliases, credentials, invalid repository identities, unsafe paths, and unsupported source types. GitHub shorthand SHALL follow the workspace command's `github.com` conversion and validation rules. Every mutation SHALL validate and replace the complete manifest atomically.

#### Scenario: Add a GitHub source to a pack

- **WHEN** an author runs `luna pack add source github awesome-copilot github/awesome-copilot --ref main`
- **THEN** `pack.yml` contains alias `awesome-copilot` with `type: git`, the expanded GitHub URL, and `refs/heads/main`

#### Scenario: Require a ref

- **WHEN** an author omits `--ref` while adding a pack-defined source
- **THEN** LunaPack returns a non-success result with a complete corrective command and leaves `pack.yml` unchanged

#### Scenario: Remove an unreferenced pack source

- **WHEN** an author runs `luna pack remove source <name>` for an alias referenced by no managed file
- **THEN** LunaPack removes that source and preserves the remaining manifest

#### Scenario: Reject removal of a referenced pack source

- **WHEN** an author removes an alias still referenced by one or more managed files
- **THEN** LunaPack reports the reference count and leaves `pack.yml` unchanged
