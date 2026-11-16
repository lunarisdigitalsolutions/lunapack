# Spec Delta

## MODIFIED Requirements

### Requirement: List available packages

LunaPack SHALL provide `luna discover` to list directly installable cataloged
pack releases. A release SHALL be composite when its manifest declares one or
more `packs` references and raw otherwise. By default, the command SHALL include
both types and emit the highest directly installable version for each pack ID.
The `--only-composites` option or an enabled `discover.only-composites`
preference SHALL restrict results to composite packs. Releases with
`directInstallable: false` SHALL always be excluded, including when
`--allow-draft` is specified. These filters SHALL apply before version selection.
The `--versions <count>` option SHALL accept values from one through 10 and
emit up to that many distinct eligible releases per package in descending
Semantic Version order. Results SHALL use separate Pack and Version columns,
include the optional description preview, order package IDs ordinally, and
produce an empty result list when no eligible packages are available.

#### Scenario: List latest package versions

- **WHEN** cataloged sources contain multiple directly installable versions of
  one pack ID
- **THEN** `luna discover` emits that ID's highest directly installable version

#### Scenario: List requested recent package versions

- **WHEN** a user runs `luna discover --versions 3` for a catalog containing
  three eligible versions of one pack ID
- **THEN** the command emits those versions in descending Semantic Version
  order with separate Pack and Version columns

#### Scenario: Shorten a package description

- **WHEN** a listed package has a description longer than 80 characters
- **THEN** its result includes a preview no longer than 80 characters,
  including any truncation marker

#### Scenario: Show raw and composite packs by default

- **WHEN** cataloged directly installable releases include packs both with and
  without `packs` references and composite-only filtering is disabled
- **THEN** `luna discover` includes both composite and raw packs

#### Scenario: Show only composites

- **WHEN** a user runs `luna discover --only-composites` or its effective
  preference enables the filter
- **THEN** `luna discover` excludes raw packs

#### Scenario: Never list a dependency-only pack

- **WHEN** a cataloged pack declares `directInstallable: false` and a user runs
  `luna discover --allow-draft`
- **THEN** `luna discover` omits that pack

### Requirement: Search available packages by metadata relevance

LunaPack SHALL provide `luna search <term>` to list directly installable
cataloged pack releases whose ID, optional description, or tag matches the
non-empty search term. By default, the command SHALL emit the latest eligible
matching release for each pack ID. The `--only-composites` option or an enabled
`search.only-composites` preference SHALL restrict results to packs declaring
one or more `packs` references; otherwise both composite and raw packs SHALL be
included. Releases with `directInstallable: false` SHALL always be excluded,
including when `--allow-draft` is specified. Type and installability filters
SHALL apply before version selection and relevance ranking. The
`--versions <count>` option SHALL accept values from one through 10 and emit up
to that many distinct eligible matching releases per pack in descending
Semantic Version order. Each result SHALL contain separate pack ID and version
columns and a description preview when one exists; the preview SHALL be no
longer than 80 characters.

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

- **WHEN** a search term appears in a directly installable cataloged pack's
  optional description
- **THEN** `luna search` includes that pack in its results

#### Scenario: List requested matching versions in search results

- **WHEN** multiple eligible cataloged versions of a pack ID match a search
  term and a user requests a version count
- **THEN** `luna search` emits up to that many versions in descending Semantic
  Version order

#### Scenario: Show raw and composite matches by default

- **WHEN** matching directly installable releases include packs both with and
  without `packs` references and composite-only filtering is disabled
- **THEN** `luna search` includes both composite and raw packs

#### Scenario: Search only composites

- **WHEN** a user runs `luna search <term> --only-composites` or its effective
  preference enables the filter
- **THEN** `luna search` excludes raw packs

#### Scenario: Never search dependency-only packs

- **WHEN** a matching cataloged pack declares `directInstallable: false` and a
  user runs `luna search <term> --allow-draft`
- **THEN** `luna search` omits that pack

## ADDED Requirements

### Requirement: Inspect a dependency-only pack

`luna inspect <pack-id>[@<version>]` SHALL resolve an explicitly requested
cataloged pack regardless of its discover/search visibility and SHALL display
its direct-installability state.

#### Scenario: Inspect a hidden dependency-only pack

- **WHEN** a user inspects a cataloged pack whose manifest declares
  `directInstallable: false`
- **THEN** LunaPack displays that pack's details and reports it is not directly
  installable
