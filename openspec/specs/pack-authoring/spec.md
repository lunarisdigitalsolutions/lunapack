# pack-authoring Specification

## Purpose

Define CLI behavior for creating, inspecting, validating, and incrementally
maintaining local LunaPack pack manifests without requiring direct YAML edits.

## Requirements

### Requirement: Initialize a valid pack manifest

`luna pack init` SHALL create `pack.yml` in the selected workspace only after
collecting every required manifest value. It SHALL accept `--id` and `--version`,
default the version to `1.0.0`, prompt for missing required values only when an
interactive terminal is available, and refuse to overwrite an existing
manifest. The generated manifest SHALL pass the published pack schema and use
canonical YAML property names. Pack IDs SHALL use alphanumeric segments joined
by single hyphens. The interactive license prompt SHALL default to `MIT`.

#### Scenario: Initialize from options

- **WHEN** an author runs `luna pack init --id dotnet-api --version 1.0.0`
- **THEN** LunaPack creates a valid `pack.yml` without prompting

#### Scenario: Initialize interactively

- **WHEN** an author runs `luna pack init` in an interactive terminal
- **THEN** LunaPack prompts for the missing pack ID, offers `1.0.0` as the
  version default and `MIT` as the license default, and writes only after the
  resulting manifest validates

#### Scenario: Reject an invalid pack ID

- **WHEN** an author supplies a pack ID with any non-alphanumeric, non-hyphen
  character, repeated hyphens, or leading or trailing hyphen
- **THEN** LunaPack rejects the manifest and preserves any existing file

#### Scenario: Reject missing non-interactive input

- **WHEN** required initialization input is missing and no interactive terminal
  is available
- **THEN** LunaPack reports each missing option and does not create `pack.yml`

#### Scenario: Preserve an existing manifest

- **WHEN** `pack.yml` already exists in the selected workspace
- **THEN** initialization fails without changing the file

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

#### Scenario: Reject an unknown source alias

- **WHEN** an author adds managed content using a source alias absent from the pack manifest
- **THEN** LunaPack reports the unknown alias, recommends commands to add it, and leaves `pack.yml` unchanged

#### Scenario: Remove an exact selector

- **WHEN** an author runs `luna pack rm "docs/**/*.md"` and exactly one selector
  has that value
- **THEN** LunaPack removes that managed-file entry

#### Scenario: Reject an unsafe path

- **WHEN** an add or remove command receives a rooted or escaping source or target path
- **THEN** LunaPack reports the invalid input and leaves `pack.yml` unchanged

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

### Requirement: Author composite references

The CLI SHALL let authors list, add, replace, and remove composite pack
references. It SHALL support exact versions, parameter bindings, and disabled
lifecycle hooks accepted by the published schema.

#### Scenario: Add a composite reference

- **WHEN** an author adds a pack ID with an exact version, parameter bindings,
  and disabled hooks
- **THEN** LunaPack writes one schema-valid composite reference

#### Scenario: Replace a composite reference

- **WHEN** an author sets an existing pack ID to a different exact version or
  bindings
- **THEN** LunaPack replaces that reference without creating a duplicate ID

#### Scenario: Remove a composite reference

- **WHEN** an author removes an existing composite pack ID
- **THEN** LunaPack removes only that reference

### Requirement: Author ordered typed lifecycle hooks

The CLI SHALL let authors list, append, replace, and remove typed hooks for `preInstall`, `postInstall`, `preUpdate`, `postUpdate`, `preUninstall`, and `postUninstall`. `luna pack add hook script command <event> <command> [arguments...]` SHALL append a command-form script, `luna pack add hook script file <event> <file> <runner> [arguments...]` SHALL append a file-form script, and `luna pack add hook instruction <event> <file>` SHALL append an instruction with optional `--templating`. Add commands SHALL accept `--replace <position>` to replace the hook at a one-based event position instead of appending. `luna pack hooks` SHALL list hooks in event and declaration order with one-based positions. `luna pack rm hook <event> <position>` SHALL remove exactly one positioned hook. The CLI SHALL preserve safe pack-relative paths and SHALL not execute or display hooks while authoring them.

#### Scenario: Append a command-form script hook

- **WHEN** an author runs `luna pack add hook script command postInstall npm install`
- **THEN** LunaPack appends a `script` hook that stores `npm` as `command` and `install` as its first argument

#### Scenario: Append a templated instruction hook

- **WHEN** an author runs `luna pack add hook instruction preInstall instructions/setup.md --templating`
- **THEN** LunaPack appends an `instruction` hook with the normalized file and enabled templating

#### Scenario: Replace a positioned hook

- **WHEN** an author adds a hook with `--replace 2` for an event containing at least two hooks
- **THEN** LunaPack replaces only the second hook and preserves the order of every other declaration

#### Scenario: Remove a positioned hook

- **WHEN** an author runs `luna pack rm hook postInstall 1`
- **THEN** LunaPack removes only the first `postInstall` hook

#### Scenario: List ordered hooks

- **WHEN** an author runs `luna pack hooks`
- **THEN** LunaPack lists each typed hook in lifecycle-event and declaration order with its one-based event position

### Requirement: Maintain pack metadata, tags, and parameters

The CLI SHALL provide `set`, `list`, and `rm` operations as applicable for every
pack metadata property, tag, and parameter declaration accepted by the
published schema. It SHALL preserve value types, enum ordering, and optional
display metadata.

#### Scenario: Set scalar metadata

- **WHEN** an author runs `luna pack set description "ASP.NET API standards"`
- **THEN** LunaPack updates only the manifest description

#### Scenario: Maintain tags

- **WHEN** an author adds or removes a valid tag
- **THEN** LunaPack persists a unique tag collection within schema limits

#### Scenario: Maintain a parameter

- **WHEN** an author sets a string, boolean, or enum parameter with supported
  prompt metadata
- **THEN** LunaPack persists a schema-valid typed declaration

#### Scenario: Protect required metadata

- **WHEN** an author attempts to remove required metadata or set an invalid
  version
- **THEN** LunaPack rejects the operation and leaves `pack.yml` unchanged

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

### Requirement: Persist mutations safely

Every authoring mutation SHALL load the complete manifest, apply one requested
change in memory, validate the complete candidate against the published schema,
and replace `pack.yml` atomically only after validation succeeds. Existing
unrelated values and supported human edits SHALL remain semantically unchanged.

#### Scenario: Reject an invalid candidate

- **WHEN** a requested mutation would make the complete manifest invalid
- **THEN** LunaPack reports validation errors and preserves the original bytes

#### Scenario: Fail during replacement

- **WHEN** writing or replacing the candidate manifest fails
- **THEN** LunaPack reports failure and preserves the last complete manifest

#### Scenario: Preserve unrelated content

- **WHEN** a valid manifest contains supported values unrelated to a successful
  mutation
- **THEN** those values retain their meaning after serialization
