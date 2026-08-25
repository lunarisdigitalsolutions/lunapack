## MODIFIED Requirements

### Requirement: Browse configured local sources for pack manifests

LunaPack SHALL build a package catalog from the sources in a schema-valid `lunapack.yml`. For each configured local source, it SHALL recursively inspect the source directory for files named `pack.yml`, treat the containing directory as the candidate pack root, and include the candidate only when its manifest parses and conforms to the pack-manifest schema. A candidate's pack ID does not need to match its directory name.

For each configured Git source, LunaPack SHALL catalog schema-valid `pack.yml` candidates available at that source's resolved commit. A Git candidate's pack root SHALL be its repository-relative containing directory. Local and Git candidates SHALL use the same semantic-version selection and configured-source precedence rules.

#### Scenario: Discover nested local packs

- **WHEN** a configured local source contains schema-valid `pack.yml` files in nested directories
- **THEN** LunaPack includes each candidate in the package catalog with its containing directory as the pack root

#### Scenario: Catalog a Git-source pack

- **WHEN** a configured Git source contains a schema-valid pack manifest at its resolved commit
- **THEN** LunaPack includes the manifest's pack in the package catalog with its repository-relative containing directory as the pack root

#### Scenario: Exclude an invalid discovered manifest

- **WHEN** recursive browsing finds a `pack.yml` that cannot be read as a schema-valid pack manifest
- **THEN** LunaPack excludes that candidate and continues cataloging valid candidates from the source

#### Scenario: Fail when catalog configuration is unavailable

- **WHEN** a user runs a catalog command without a schema-valid `lunapack.yml`
- **THEN** LunaPack returns a non-success result and produces no package results
