## MODIFIED Requirements

### Requirement: Publish local pack-manifest schema

The repository SHALL publish a JSON Schema under `projects/schema/` for
`pack.yml`. The schema SHALL require a pack identity, semantic version,
non-empty author, and non-empty license. It SHALL allow empty managed-file,
composite-pack, and lifecycle-hook collections for incremental authoring. It
SHALL allow optional non-empty name and homepage metadata, an optional
human-readable package description, and up to 15 unique, non-empty tags. A
complete distributable pack MAY declare managed-file entries, composite pack
references, lifecycle hooks, or any combination of them. Each composite
reference SHALL contain a pack ID and an exact Semantic Version and MAY bind
identifier-named string or boolean parameters for its referenced pack.
Managed-file selectors MAY set `template` to opt into Scriban parsing; it
defaults to false. Pack manifests SHALL not contain source configuration.

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

- **WHEN** the schema validates a pack manifest with a description and a managed-file declaration, composite pack reference, or lifecycle hook
- **THEN** validation succeeds

#### Scenario: Validate bounded pack tags

- **WHEN** the schema validates a pack manifest with up to 15 unique, non-empty tags
- **THEN** validation succeeds

#### Scenario: Reject excessive pack tags

- **WHEN** the schema validates a pack manifest with more than 15 tags
- **THEN** validation fails

#### Scenario: Preserve file-only manifests

- **WHEN** the schema validates an existing complete pack manifest that declares managed files but no composite references or lifecycle hooks
- **THEN** validation succeeds

#### Scenario: Validate a contentless composite manifest

- **WHEN** the schema validates a pack manifest with one or more composite references and no managed files or lifecycle hooks
- **THEN** validation succeeds

#### Scenario: Validate an instruction-only manifest

- **WHEN** the schema validates a pack manifest with one or more lifecycle hooks and no managed files or composite references
- **THEN** validation succeeds

#### Scenario: Reject an incomplete or unpinned composite reference

- **WHEN** the schema validates a pack manifest without a managed-file, composite, or lifecycle-hook declaration, or with a composite reference lacking an exact version
- **THEN** validation fails

#### Scenario: Validate composite reference parameter bindings

- **WHEN** a composite reference binds identifier-named string or boolean parameters
- **THEN** the pack manifest is valid

#### Scenario: Reject a source declaration in a pack manifest

- **WHEN** the schema validates a pack manifest containing source configuration
- **THEN** validation fails

#### Scenario: Preserve a managed file without template parsing

- **WHEN** a managed-file selector omits `template`
- **THEN** the manifest is valid and the selector defaults to non-template handling

## ADDED Requirements

### Requirement: Define typed lifecycle hooks

The `pack.yml` schema SHALL allow an optional `hooks` mapping with `preInstall`, `postInstall`, `preUpdate`, and `postUpdate` properties. Each event SHALL contain a non-empty ordered list of typed hook declarations. A `script` hook SHALL select exactly one execution form: a non-empty pack-relative `file` with a non-empty `runner`, or a non-empty external `command`; both forms MAY contain an ordered `arguments` array of strings and a non-empty `description`. An `instruction` hook SHALL contain a non-empty safe pack-relative `.md` file and MAY set `templating`, which SHALL default to false. The schema SHALL reject unknown events, hook types, and properties; unsafe file paths; mixed or incomplete hook variants; and the removed top-level `scripts` property. Existing pack manifests that omit both `scripts` and `hooks` SHALL remain valid.

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

## REMOVED Requirements

### Requirement: Define pack lifecycle scripts

**Reason**: Script-only event properties cannot preserve order across multiple script and instruction hooks.

**Migration**: Move each `scripts.<event>` declaration to an item in `hooks.<event>`, add `type: script`, and preserve its execution-form properties.
