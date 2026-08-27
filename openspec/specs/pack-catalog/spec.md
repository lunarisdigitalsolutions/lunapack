# pack-catalog Specification

## Purpose

Provide deterministic local package catalog discovery and search for packs
available through a consumer project's configured LunaPack sources.

## Requirements

### Requirement: Browse configured local sources for pack manifests

LunaPack SHALL build a package catalog from the sources in a schema-valid `lunapack.yml`. For each configured local source, it SHALL recursively inspect the source directory for files named `pack.yml`, treat the containing directory as the candidate pack root, and include the candidate only when its manifest parses and conforms to the pack-manifest schema. A candidate's pack ID does not need to match its directory name.

For each configured Git source, LunaPack SHALL catalog schema-valid `pack.yml` candidates available at that source's resolved commit. A Git candidate's pack root SHALL be its repository-relative containing directory. Local and Git candidates SHALL use the same semantic-version selection and configured-source precedence rules.

#### Scenario: Discover nested local packs

- **WHEN** a configured local source contains schema-valid `pack.yml` files in
  nested directories
- **THEN** LunaPack includes each candidate in the package catalog with its
  containing directory as its pack root

#### Scenario: Catalog a Git-source pack

- **WHEN** a configured Git source contains a schema-valid pack manifest at its resolved commit
- **THEN** LunaPack includes the manifest's pack in the package catalog with its repository-relative containing directory as the pack root

#### Scenario: Exclude an invalid discovered manifest

- **WHEN** recursive browsing finds a `pack.yml` that cannot be read as a
  schema-valid pack manifest
- **THEN** LunaPack excludes that candidate and continues cataloging valid
  candidates from the source

#### Scenario: Fail when catalog configuration is unavailable

- **WHEN** a user runs a catalog command without a schema-valid `lunapack.yml`
- **THEN** LunaPack returns a non-success result and produces no package results

### Requirement: Inspect managed-file targets with effective remapping

`luna inspect <pack-id>` SHALL display the pack's managed-file targets in a dedicated, readable list without displaying their manifest source selectors. For each target affected by valid global remapping in the inspected project's `lunapack.yml`, the list SHALL display the declared target and the effective target as `<declared-target> -> <effective-target>`. Targets unaffected by global remapping SHALL display only their declared target.

#### Scenario: Inspect a pack with an ADR directory mapping

- **WHEN** a project globally remaps `docs/adr` and a consumer runs `luna inspect madr-adr-template`
- **THEN** inspection lists `docs/adr/template.md ->` followed by the remapped effective target, without listing `templates/template.md`

#### Scenario: Inspect a pack without matching remapping

- **WHEN** a pack's managed target does not match configured global remapping
- **THEN** inspection lists only that declared target

### Requirement: List available packages

LunaPack SHALL provide `luna discover` to list all cataloged package IDs. By
default, the command SHALL emit the highest available semantic version for each
package ID. The `--versions <count>` option SHALL accept values from one through
10 and emit up to that many distinct releases per package in descending Semantic
Version order. Results SHALL use separate Pack and Version columns, include the
optional description preview, order package IDs ordinally, and produce an empty
result list when no valid packages are available.

#### Scenario: List latest package versions

- **WHEN** cataloged local sources contain multiple versions of the same pack ID
- **THEN** `luna discover` emits one result for that ID with the highest
  semantic version

#### Scenario: List requested recent package versions

- **WHEN** a user runs `luna discover --versions 3` for a catalog containing
  three versions of one pack ID
- **THEN** the command emits those versions in descending Semantic Version
  order with separate Pack and Version columns

#### Scenario: Shorten a package description

- **WHEN** a listed package has a description longer than 80 characters
- **THEN** its result includes a preview no longer than 80 characters,
  including any truncation marker

### Requirement: Search available packages by metadata relevance

LunaPack SHALL provide `luna search <term>` to list every cataloged pack whose
ID, optional description, or tag matches the non-empty search term. By default,
the command SHALL emit the latest matching release for each pack ID. The
`--versions <count>` option SHALL accept values from one through 10 and emit up
to that many distinct matching releases per pack in descending Semantic Version
order. Each result SHALL contain separate pack ID and version columns and a
description preview when one exists; the preview SHALL be no longer than 80
characters.

LunaPack SHALL rank matches locally and emit results in descending relevance.
After invariant case normalization, exact ID matches SHALL rank above ID-prefix
matches, which SHALL rank above other ID-substring matches, which SHALL rank
above description phrase matches, which SHALL rank above matches formed from
all query terms. Equal-relevance results SHALL use ordinal pack ID, descending
semantic version, configured source order, and ordinal pack-root path as
tie-breakers.

#### Scenario: Prefer an exact package ID match

- **WHEN** a search term exactly matches one pack ID and another pack only
  matches the term in its description
- **THEN** `luna search` emits the exact-ID result before the
  description-match result

#### Scenario: Search a package description

- **WHEN** a search term appears in a cataloged pack's optional description
- **THEN** `luna search` includes that pack in its results

#### Scenario: List requested matching versions in search results

- **WHEN** multiple cataloged versions of a pack ID match a search term and a
  user requests a version count
- **THEN** `luna search` emits up to that many versions in descending Semantic
  Version order

### Requirement: Inspect ordered lifecycle hooks

`luna inspect <pack-id>[@<version>]` SHALL include a lifecycle hooks section when the resolved pack declares hooks. The section SHALL list events in `preInstall`, `postInstall`, `preUpdate`, and `postUpdate` order and list each event's hooks in declaration order. For a script hook, inspection SHALL show its optional description and exact executable and ordered arguments. For an instruction hook, inspection SHALL show its pack-relative file and effective templating state. Inspection SHALL also list each composite reference's disabled lifecycle events. When a pack declares no hooks or a reference suppresses no events, inspection SHALL state that explicitly.

#### Scenario: Inspect mixed ordered hooks

- **WHEN** a resolved pack event declares instruction and script hooks and the user inspects that release
- **THEN** inspection lists both typed hooks in declaration order with their type-specific properties

#### Scenario: Inspect a pack without hooks

- **WHEN** a resolved pack declares no lifecycle hooks
- **THEN** inspection reports that the pack has no lifecycle hooks

#### Scenario: Inspect transient hook suppression

- **WHEN** a pack reference disables lifecycle events for its transient pack
- **THEN** inspection lists the referenced pack ID and each disabled lifecycle event

### Requirement: Guide successful catalog exploration

Successful catalog commands SHALL report their result before rendering
recommendations. Recommendations SHALL use the inspected pack ID when known and
replacement tokens when no single concrete value is available.

#### Scenario: Discover available packs

- **WHEN** `luna discover` successfully displays one or more pack releases
- **THEN** Luna reports the displayed pack count and recommends `luna install
<pack>`

#### Scenario: Search available packs

- **WHEN** `luna search <keyword>` successfully displays one or more matching
  pack releases
- **THEN** Luna reports the displayed match count and recommends `luna inspect
<pack>` and `luna install <pack>`

#### Scenario: Inspect a pack

- **WHEN** `luna inspect <pack-reference>` successfully displays pack details
- **THEN** Luna recommends installing that resolved pack ID and running `luna
discover`

### Requirement: Guide recovery from an unresolved catalog pack

When inspection cannot resolve a syntactically valid pack reference from
configured sources, Luna SHALL preserve the primary non-success result and
append commands that help find an available pack.

#### Scenario: Inspect an unknown pack

- **WHEN** a user runs `luna inspect unknown-pack` and no configured source
  provides that pack
- **THEN** Luna reports that the pack was not found and recommends `luna search
unknown-pack` followed by `luna discover`
