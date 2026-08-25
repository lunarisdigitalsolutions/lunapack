# Local Pack Lifecycle Delta

## ADDED Requirements

### Requirement: Select a cataloged version for installation

LunaPack SHALL accept both `lunapack install <pack-id>` and
`lunapack install <pack-id>@<version>`. It SHALL resolve package candidates from
the configured source catalog. Without an explicit version, LunaPack SHALL select
the highest available semantic version according to Semantic Versioning
precedence. With an explicit version, LunaPack SHALL select that available
version. When candidates have equal version precedence, LunaPack SHALL select the
candidate from the earliest configured source.

#### Scenario: Install an explicit version

- **WHEN** a configured local source catalogs multiple versions of a pack and a
  user runs `lunapack install <pack-id>@<version>` for one available version
- **THEN** LunaPack installs that version and records the selected version in
  `lunapack.yml`

#### Scenario: Install the latest available version

- **WHEN** a configured local source catalogs multiple versions of a pack and a
  user runs `lunapack install <pack-id>` without a version
- **THEN** LunaPack installs the highest available semantic version and records
  that version in `lunapack.yml`

#### Scenario: Reject an unavailable requested version

- **WHEN** a user requests a package version that is absent from the configured
  source catalog
- **THEN** LunaPack returns a non-success result and does not change project files
  or `lunapack.yml`

#### Scenario: Prefer the earliest configured source for equal versions

- **WHEN** multiple configured sources provide the same package ID and version
- **THEN** LunaPack installs the candidate from the earliest configured source
