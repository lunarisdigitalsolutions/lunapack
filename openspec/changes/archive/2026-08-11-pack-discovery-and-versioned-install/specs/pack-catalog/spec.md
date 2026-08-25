# Pack Catalog Delta

## Purpose

Provide deterministic local package catalog discovery and search for packs
available through a consumer project's configured LunaPack sources.

## ADDED Requirements

### Requirement: Browse configured local sources for pack manifests

LunaPack SHALL build a package catalog from the sources in a schema-valid
`lunapack.yml`. For each configured local source, it SHALL recursively inspect the
source directory for files named `pack.yml`, treat the containing directory as
the candidate pack root, and include the candidate only when its manifest
parses and conforms to the pack-manifest schema. A candidate's pack ID does not
need to match its directory name.

#### Scenario: Discover nested local packs

- **WHEN** a configured local source contains schema-valid `pack.yml` files in
  nested directories
- **THEN** LunaPack includes each candidate in the package catalog with its
  containing directory as its pack root

#### Scenario: Exclude an invalid discovered manifest

- **WHEN** recursive browsing finds a `pack.yml` that cannot be read as a
  schema-valid pack manifest
- **THEN** LunaPack excludes that candidate and continues cataloging valid
  candidates from the source

#### Scenario: Fail when catalog configuration is unavailable

- **WHEN** a user runs a catalog command without a schema-valid `lunapack.yml`
- **THEN** LunaPack returns a non-success result and produces no package results

### Requirement: List available packages

LunaPack SHALL provide `lunapack discover` to list all cataloged package IDs. The
command SHALL emit one compact result per package ID containing the ID, the
highest available semantic version, and the optional description preview. It
SHALL order results by ordinal package ID and SHALL produce an empty result
list when no valid packages are available.

#### Scenario: List latest package versions

- **WHEN** cataloged local sources contain multiple versions of the same pack ID
- **THEN** `lunapack discover` emits one result for that ID with the highest
  semantic version

#### Scenario: Shorten a package description

- **WHEN** a listed package has a description longer than 80 characters
- **THEN** its result includes a preview no longer than 80 characters,
  including any truncation marker

### Requirement: Search available packages by metadata relevance

LunaPack SHALL provide `lunapack search <term>` to list every cataloged pack whose
ID or optional description matches the non-empty search term. Each compact
result SHALL contain the pack ID, version, and description preview when a
description exists; the preview SHALL be no longer than 80 characters.

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
- **THEN** `lunapack search` emits the exact-ID result before the
  description-match result

#### Scenario: Search a package description

- **WHEN** a search term appears in a cataloged pack's optional description
- **THEN** `lunapack search` includes that pack in its results

#### Scenario: Preserve matching versions in search results

- **WHEN** multiple cataloged versions of a pack ID match a search term
- **THEN** `lunapack search` emits each matching version in relevance order
