# manifest-schemas Specification

## Purpose

Define machine-readable contracts for LunaPack project configuration, lock state, and local pack manifests.

## Requirements

### Requirement: Publish project-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `lunapack.yml`. The schema SHALL require schema version `1`, define local and Git source entries, and define requested root pack references. A Git source SHALL require a repository URL and SHALL allow optional `ref`, optional repository-relative `path`, and optional `timeoutSeconds` from 1 through 300. Requested root pack references SHALL include an ID and MAY include an explicit Semantic Version request. The schema SHALL reject absolute local source paths, unsafe Git source paths, unsupported source types, resolved source provenance, managed file ownership, digests, and unknown required-state omissions. Existing valid local-source configuration SHALL remain valid.

#### Scenario: Validate an initialized manifest

- **WHEN** the schema validates a manifest created by `luna init`
- **THEN** validation succeeds

#### Scenario: Validate a Git source

- **WHEN** the schema validates a Git source with a repository URL and optional valid ref, path, and timeout
- **THEN** validation succeeds

#### Scenario: Reject an unsupported source type

- **WHEN** the schema validates a manifest containing a non-local source type
- **THEN** validation fails

#### Scenario: Reject an unsafe Git source path

- **WHEN** the schema validates a Git source path that is absolute or escapes the repository root
- **THEN** validation fails

#### Scenario: Reject an absolute local source path

- **WHEN** the schema validates a local source path rooted at a filesystem drive, UNC location, or root directory
- **THEN** validation fails

#### Scenario: Reject resolved installation state in configuration

- **WHEN** the schema validates `lunapack.yml` containing a resolved source path, managed-file list, or content digest
- **THEN** validation fails

### Requirement: Publish project lock-file schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `lunapack-lock.yml`. The schema SHALL require its explicit schema version and a resolved pack graph with exact pack identity and version, source provenance, composite references, and managed target-path SHA-256 records. Git-sourced pack provenance SHALL record the repository URL, requested ref when configured, configured repository path when configured, and the resolved commit SHA. A pack that uses an external source SHALL record each used pack-local source alias, its authoritative workspace source identifier, normalized fingerprint, canonical requested ref, and resolved commit. Each externally sourced managed-file record SHALL identify its owning pack, pack version, pack-local source alias, workspace source identifier, fingerprint, source-relative path, effective target, and installed content hash. The schema SHALL reject unknown lock schema versions and incomplete resolved pack or external-source records. Existing valid local-source and Git-source lock records that contain no external-source provenance SHALL remain valid.

#### Scenario: Validate resolved composite lock state

- **WHEN** the lock schema validates the state produced for a composite pack and its transitive packs
- **THEN** validation succeeds

#### Scenario: Validate Git-resolved lock state

- **WHEN** the lock schema validates a Git-sourced pack record with its repository URL and resolved commit SHA
- **THEN** validation succeeds

#### Scenario: Validate external-source provenance

- **WHEN** the lock schema validates an externally sourced file and its pack alias mapping with all required identity, revision, path, ownership, and hash fields
- **THEN** validation succeeds

#### Scenario: Reject incomplete resolved state

- **WHEN** the lock schema validates a resolved pack record without source provenance, an exact version, or a required managed-file digest
- **THEN** validation fails

#### Scenario: Reject Git provenance without a resolved commit

- **WHEN** the lock schema validates a Git-sourced pack or external-source record without a resolved commit SHA
- **THEN** validation fails

### Requirement: Publish local pack-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for `pack.yml`. The schema SHALL require a pack identity, semantic version, non-empty author, and non-empty license. It SHALL allow empty managed-file and composite-pack collections for incremental authoring. It SHALL allow optional non-empty name and homepage metadata, an optional human-readable package description, and up to 15 unique, non-empty tags. A complete distributable pack MAY declare managed-file entries, composite pack references, or both. Each composite reference SHALL contain a pack ID and an exact Semantic Version and MAY bind identifier-named string or boolean parameters for its referenced pack. Managed-file selectors MAY set `template` to opt into Scriban parsing; it defaults to false.

The schema SHALL allow an optional `sources` mapping whose keys are pack-local aliases and whose values are Git source declarations. Each declaration SHALL require `type: git`, a credential-free repository URL, and an explicit ref, and MAY contain a safe repository-relative base `path` and non-empty `description`. Pack-defined local sources and credential placeholders SHALL be invalid. Managed-file selectors MAY name a declared source alias and MAY select a file, recursive directory, or glob with repeatable exclusion patterns and optional flattening. Source and exclusion paths SHALL be relative and SHALL not escape the resolved source root. A selector without a source alias SHALL continue to resolve from the pack source. Lifecycle scripts SHALL resolve only from the pack source and SHALL not reference an external source.

#### Scenario: Reject a manifest without required attribution

- **WHEN** the schema validates a manifest without author or license metadata
- **THEN** validation fails

#### Scenario: Validate optional metadata

- **WHEN** the schema validates a manifest with non-empty name, author, homepage,
  and license values
- **THEN** validation succeeds

#### Scenario: Reject a pack manifest without attribution

- **WHEN** a pack manifest includes an empty author or license value
- **THEN** validation fails because attribution must be non-empty

#### Scenario: Reject invalid optional metadata

- **WHEN** optional name or homepage metadata is empty or the homepage is not a
  supported absolute URI
- **THEN** validation fails

#### Scenario: Preserve manifests without a description

- **WHEN** the schema validates an existing complete pack manifest without a description
- **THEN** validation succeeds

#### Scenario: Reject an incomplete pack identity

- **WHEN** the schema validates a pack manifest without an ID or version
- **THEN** validation fails

#### Scenario: Reject an incomplete pack manifest

- **WHEN** the schema validates a pack manifest without a complete identity or
  with an incomplete managed-file declaration
- **THEN** validation fails

#### Scenario: Validate the dotnet gitignore pack manifest

- **WHEN** the schema validates the repository's `dotnet-gitignore` pack manifest
- **THEN** validation succeeds

#### Scenario: Validate a manifest with a description

- **WHEN** the schema validates a pack manifest with a description and a managed-file declaration or composite pack reference
- **THEN** validation succeeds

#### Scenario: Validate bounded pack tags

- **WHEN** the schema validates a pack manifest with up to 15 unique, non-empty tags
- **THEN** validation succeeds

#### Scenario: Reject excessive pack tags

- **WHEN** the schema validates a pack manifest with more than 15 tags
- **THEN** validation fails

#### Scenario: Preserve file-only manifests

- **WHEN** the schema validates an existing complete pack manifest that declares managed files but no composite references or external sources
- **THEN** validation succeeds

#### Scenario: Validate a contentless composite manifest

- **WHEN** the schema validates a pack manifest with one or more composite references and no managed files
- **THEN** validation succeeds

#### Scenario: Reject an incomplete or unpinned composite reference

- **WHEN** the schema validates a pack manifest without a managed-file or composite declaration, or with a composite reference lacking an exact version
- **THEN** validation fails

#### Scenario: Validate composite reference parameter bindings

- **WHEN** a composite reference binds identifier-named string or boolean parameters
- **THEN** the pack manifest is valid

#### Scenario: Reject a source declaration in a pack manifest

- **WHEN** a pack manifest declares a source that is local, lacks an explicit ref, contains credentials, or otherwise violates the pack-defined Git source contract
- **THEN** validation fails

#### Scenario: Validate a pack-defined Git source

- **WHEN** a pack manifest declares a credential-free Git source with an explicit ref and a managed file references its alias
- **THEN** validation succeeds

#### Scenario: Reject a pack-defined local source

- **WHEN** a pack manifest declares a source with `type: local`
- **THEN** validation fails

#### Scenario: Reject an unpinned pack-defined source

- **WHEN** a pack manifest declares a Git source without a ref
- **THEN** validation fails

#### Scenario: Reject an unknown source alias

- **WHEN** a managed-file selector names an alias absent from the pack's `sources` mapping
- **THEN** validation fails

#### Scenario: Reject an external lifecycle script

- **WHEN** a lifecycle script attempts to select its file from a pack-defined external source
- **THEN** validation fails

#### Scenario: Preserve a managed file without template parsing

- **WHEN** a managed-file selector omits `template`
- **THEN** the manifest is valid and the selector defaults to non-template handling

### Requirement: Define Luna Link configuration

The version-1 `lunapack.yml` JSON Schema SHALL allow an optional `links` mapping
keyed by a LunaPack pack-ID-shaped name. Each link SHALL require a configured
source name and a non-empty unique `includes` array. It SHALL allow unique
`excludes`, an optional project-relative base `path`, optional project-relative
`target`, optional Git `ref`, optional project-relative `stripPrefix`, and
optional boolean `flatten`. The schema SHALL reject unknown properties, rooted
or syntactically escaping paths, empty selectors, and invalid names. Existing
valid version-1 configurations that omit links SHALL remain valid.

#### Scenario: Validate a complete link definition

- **WHEN** the schema validates a link with source, includes, excludes, base path, target, Git ref, strip prefix, and flattening
- **THEN** validation succeeds when every value has the required type and safe syntax

#### Scenario: Validate existing configuration without links

- **WHEN** the schema validates an existing version-1 project configuration that omits `links`
- **THEN** validation succeeds without a schema-version migration

#### Scenario: Reject an unsafe link path

- **WHEN** a link base path, target, or strip prefix is rooted or contains a parent traversal segment
- **THEN** project-configuration schema validation fails

### Requirement: Define resolved Luna Link state

The current `lunapack-lock.yml` JSON Schema SHALL allow a resolved `links`
mapping keyed by link name. Each record SHALL require configured-source identity,
a canonical SHA-256 digest of the installed definition, and a non-empty
selected-file collection. Git-backed records SHALL require the effective ref and
immutable resolved commit. Each selected-file record SHALL require normalized
source path, declared target identity, effective project-relative target path,
and installed SHA-256 content digest. Existing valid lock files that omit links
SHALL remain valid.

#### Scenario: Validate resolved Git link state

- **WHEN** the lock schema validates a Git link record with source identity, definition digest, effective ref, resolved commit, and complete selected-file records
- **THEN** validation succeeds

#### Scenario: Validate resolved local link state

- **WHEN** the lock schema validates a local link record with source identity, definition digest, and complete selected-file records but no Git commit
- **THEN** validation succeeds

#### Scenario: Reject incomplete selected-file ownership

- **WHEN** a resolved link file omits its source path, declared target, effective target, or installed digest
- **THEN** lock-file schema validation fails

### Requirement: Maintain schema compatibility deliberately

The project configuration schema SHALL retain explicit schema version `1`, and the lock-file schema SHALL use its own explicit schema version. LunaPack SHALL not support the former version-1 document shape that contains resolved source provenance or managed-file ownership. Future incompatible lock-file changes SHALL use a new lock-file schema version.

#### Scenario: Reject an unknown schema version

- **WHEN** either schema validates a document with an unsupported schema version
- **THEN** validation fails

#### Scenario: Reject a former combined-state manifest

- **WHEN** LunaPack reads a version-1 `lunapack.yml` that contains resolved source provenance, managed-file ownership, or content digests
- **THEN** it rejects the document as invalid project configuration

### Requirement: Represent optional pack destinations in version-1 state

The project-configuration and lock-file schemas SHALL allow an optional,
non-empty, project-relative `destination` for directly requested packs. The
lock-file schema SHALL allow the corresponding resolved destination while
retaining every effective managed target path and digest. Existing valid
version-1 state files that omit destination metadata SHALL remain valid.

#### Scenario: Validate destination-installed pack state

- **WHEN** the schemas validate state written after a destination-installed
  pack succeeds
- **THEN** the project configuration and lock file both validate and retain the
  requested destination

#### Scenario: Validate existing state without a destination

- **WHEN** the schemas validate a pre-destination version-1 configuration and
  lock file
- **THEN** validation succeeds without a schema-version migration

#### Scenario: Reject an unsafe persisted destination

- **WHEN** either schema validates an absolute destination or one that escapes
  the project root
- **THEN** validation fails

### Requirement: Define typed pack parameters

The `pack.yml` schema SHALL allow an optional `parameters` mapping keyed by a
non-empty parameter name. Each parameter declaration SHALL require a `type` of
`string`, `bool`, or `enum`; its `required` flag SHALL default to false. An
`enum` declaration SHALL contain a non-empty, unique collection of allowed
string `values` and MAY set `multiple` to true; other parameter types SHALL
reject `values` and `multiple`. An omitted `multiple` property SHALL be
equivalent to false. A parameter MAY declare non-empty `displayName` and
`description` strings for interactive prompts. A parameter MAY define a
`default` matching its declared type. A scalar enum default SHALL be one of its
declared values. A multi-select enum default SHALL be a unique array containing
zero or more declared values. Existing valid version-1 pack manifests without
parameters or `multiple` SHALL remain valid.

#### Scenario: Validate an enum parameter declaration

- **WHEN** schema validation receives a parameter with type `enum`, a required
  flag, and distinct allowed string values
- **THEN** the pack manifest is valid

#### Scenario: Validate a multi-select enum declaration

- **WHEN** schema validation receives an enum parameter with `multiple: true`
  and distinct allowed string values
- **THEN** the pack manifest is valid

#### Scenario: Reject multiple on another parameter type

- **WHEN** schema validation receives a string or boolean parameter with a
  `multiple` property
- **THEN** the pack manifest is invalid

#### Scenario: Validate parameter display metadata

- **WHEN** a parameter declaration contains display name and description text
- **THEN** the pack manifest is valid

#### Scenario: Reject an unconstrained enum declaration

- **WHEN** schema validation receives an enum parameter without values or with
  duplicated values
- **THEN** the pack manifest is invalid

#### Scenario: Validate a typed parameter default

- **WHEN** a string or boolean parameter declares a default of the matching type
- **THEN** the pack manifest is valid

#### Scenario: Validate a multi-select enum default

- **WHEN** a multi-select enum default is an empty array or a unique array of
  values from its allowed set
- **THEN** the pack manifest is valid

#### Scenario: Reject an invalid enum default

- **WHEN** an enum default has the wrong scalar-or-array shape, contains a
  duplicate, or contains a value outside its declared values
- **THEN** the pack manifest is invalid

### Requirement: Define conditional managed files

The `pack.yml` schema SHALL allow an optional string `condition` on each
managed-file declaration. Existing manifests that omit `condition` SHALL
remain valid.

#### Scenario: Validate a managed file without a condition

- **WHEN** schema validation receives an existing managed-file declaration
  without a condition
- **THEN** the pack manifest is valid

### Requirement: Define project variables

The `lunapack.yml` schema SHALL allow an optional `variables` mapping whose
non-empty names map to string values, boolean values, or unique arrays of
strings. Arrays SHALL preserve their declared order and provide values for
multi-select enum parameters. Existing valid version-1 project configuration
without variables or array values SHALL remain valid.

#### Scenario: Validate configured template variables

- **WHEN** schema validation receives project configuration with string,
  boolean, and unique string-array variable values
- **THEN** the project manifest is valid

#### Scenario: Reject a non-scalar project variable

- **WHEN** schema validation receives a project variable whose value is not a
  string, boolean, or unique string array
- **THEN** the project manifest is invalid

### Requirement: Define multi-select composite parameter bindings

The `pack.yml` schema SHALL allow a composite reference parameter binding to
contain a unique array of strings for a referenced multi-select enum parameter.
The schema SHALL preserve existing string and boolean binding values, and
runtime validation SHALL reject an array binding for any non-multi-select
parameter or any selected value outside the referenced declaration.

#### Scenario: Validate a multi-select composite binding

- **WHEN** a composite reference binds a unique string array to a multi-select
  enum declared by its referenced pack
- **THEN** the pack manifest and runtime binding are valid

#### Scenario: Reject an incompatible composite binding

- **WHEN** a composite reference binds an array to a scalar parameter or binds
  a value outside the referenced enum declaration
- **THEN** LunaPack rejects the pack before changing project files or state

### Requirement: Define typed lifecycle hooks

The `pack.yml` schema SHALL allow an optional `hooks` mapping with `preInstall`, `postInstall`, `preUpdate`, `postUpdate`, `preUninstall`, and `postUninstall` properties. Each event SHALL contain a non-empty ordered list of typed hook declarations. A `script` hook SHALL select exactly one execution form: a non-empty pack-relative `file` with a non-empty `runner`, or a non-empty external `command`; both forms MAY contain an ordered `arguments` array of strings and a non-empty `description`. An `instruction` hook SHALL contain a non-empty safe pack-relative `.md` file and MAY set `templating`, which SHALL default to false. The schema SHALL reject unknown events, hook types, and properties; unsafe file paths; mixed or incomplete hook variants; and the removed top-level `scripts` property. Existing pack manifests that omit both `scripts` and `hooks` SHALL remain valid.

#### Scenario: Validate mixed hooks in declared order

- **WHEN** one event declares an instruction hook followed by command-form and file-form script hooks
- **THEN** the manifest is valid and preserves all three typed declarations in their declared order

#### Scenario: Default instruction templating off

- **WHEN** an instruction hook declares a safe pack-relative `.md` file and omits `templating`
- **THEN** the manifest is valid and the hook defaults to literal Markdown handling

#### Scenario: Reject an invalid typed hook

- **WHEN** a hook mixes script and instruction properties or omits properties required by its declared type
- **THEN** the manifest is invalid

#### Scenario: Reject an unsafe hook file

- **WHEN** a script or instruction hook file is rooted or contains a parent traversal segment
- **THEN** the manifest is invalid

#### Scenario: Reject the removed scripts section

- **WHEN** a pack manifest declares the former top-level `scripts` property
- **THEN** the manifest is invalid and validation directs the author to migrate declarations into typed `hooks`

### Requirement: Define lifecycle suppression on composite references

Each composite pack reference in `pack.yml` SHALL allow an optional `disabledHooks` collection containing unique values from `preInstall`, `postInstall`, `preUpdate`, `postUpdate`, `preUninstall`, and `postUninstall`. An omitted or empty collection SHALL suppress no hook. Existing composite references without `disabledHooks` SHALL remain valid.

#### Scenario: Disable selected hooks for a referenced pack

- **WHEN** a composite reference declares `preInstall` and `postInstall` in `disabledHooks`
- **THEN** the pack manifest is valid and preserves both suppressed lifecycle types

#### Scenario: Reject an unknown lifecycle type

- **WHEN** a composite reference declares an unsupported value in `disabledHooks`
- **THEN** the pack manifest is invalid

### Requirement: Represent named sources and project script trust in version-1 configuration

The `lunapack.yml` schema SHALL require every local, Git, and GitHub-derived source entry to contain a non-empty `name`. Source names SHALL be unique within one project configuration. The schema SHALL allow a project-scoped `trust` mapping containing optional unique source-name entries, optional unique pack entries, and an optional `deny` mapping whose optional `scripts` boolean defaults to `false`. Each pack entry SHALL require a configured source name and bare pack ID without a version selector. Omitted trust, denial, source-trust, and pack-trust properties SHALL represent empty grants with scripts not denied. Existing schema version `1` SHALL be retained.

#### Scenario: Validate named sources and trust

- **WHEN** a version-1 project configuration contains uniquely named sources, distinct trusted source names, and distinct source-plus-pack-ID entries
- **THEN** the project configuration is valid

#### Scenario: Validate project script denial alone

- **WHEN** a version-1 project configuration contains `trust.deny.scripts: true` without source or pack trust collections
- **THEN** the project configuration is valid and declares portable script denial

#### Scenario: Default omitted project denial off

- **WHEN** a version-1 project configuration omits `trust`, `trust.deny`, or `trust.deny.scripts`
- **THEN** the configuration is valid and does not deny scripts

#### Scenario: Reject version-specific pack trust

- **WHEN** a trusted pack entry contains an `@version` selector
- **THEN** the project configuration is invalid

#### Scenario: Reject duplicate source names

- **WHEN** two configured sources have the same ordinal name
- **THEN** the project configuration is invalid

#### Scenario: Reject pack trust without a source

- **WHEN** a trusted pack entry contains an ID but no configured source name
- **THEN** the project configuration is invalid

#### Scenario: Preserve empty trust collections

- **WHEN** a version-1 project configuration contains empty `trust.sources` and `trust.packs` collections with omitted denial
- **THEN** the project configuration is valid without a schema-version migration and does not deny scripts

### Requirement: Define cross-platform user trust settings

LunaPack SHALL define a user-settings document at `~/.lunapack/config.yml`. It SHALL contain optional global source and source-plus-pack-ID trust entries, optional global `deny.scripts` policy, and optional local-project trust records keyed by canonical absolute project directory. A local-project record SHALL support the same grants and denial policy and MAY acknowledge project-scoped source and pack declarations by their exact source identities. Acknowledgements SHALL contain only positive source and pack entries and SHALL not contain denial policy. Omitted denial SHALL default to `false`; omitted source and pack collections SHALL default to empty. Duplicate, incomplete, version-qualified, or unsafe project-path entries SHALL be invalid.

#### Scenario: Validate global and local-user trust

- **WHEN** user settings contain global trust and a local-project record keyed by a canonical project path
- **THEN** the settings are valid on the current operating system

#### Scenario: Validate user denial without grants

- **WHEN** global-user or project-local user settings contain `deny.scripts: true` without source or pack collections
- **THEN** the settings are valid and deny scripts in that scope

#### Scenario: Default omitted user denial off

- **WHEN** global-user or project-local user settings omit `deny.scripts`
- **THEN** scripts are not denied by that scope

#### Scenario: Reject denial in project acknowledgements

- **WHEN** a project acknowledgement contains a script-denial policy
- **THEN** the user settings are invalid

#### Scenario: Reject a relative local-project key

- **WHEN** a local-project trust record uses a relative project path
- **THEN** the user settings are invalid

### Requirement: Persist immutable configured-source identity for every resolved pack

The existing `lunapack-lock.yml` schema version SHALL require every resolved root and transient pack to identify its configured source by name, type, and normalized location fields. Git provenance SHALL continue to include the resolved commit. The configured-source identity used for update and trust matching SHALL exclude mutable resolution data such as a Git commit while the lock record retains that data as release provenance.

#### Scenario: Lock source identity for a local transient pack

- **WHEN** installation resolves a transient pack from a named local source
- **THEN** its lock record contains the source name, local type, normalized configured path, and pack path

#### Scenario: Lock source identity and commit for a Git pack

- **WHEN** installation resolves a pack from a named Git source
- **THEN** its lock record contains the source name, normalized URL, ref and repository path when present, plus the resolved commit
