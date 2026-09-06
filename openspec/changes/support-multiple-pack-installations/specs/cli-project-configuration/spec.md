## ADDED Requirements

### Requirement: Persist portable requested-root instances

Each requested pack entry in `lunapack.yml` SHALL represent one requested-root instance and MAY contain an alias. An omitted alias SHALL mean the pack-ID-named default instance. The configuration SHALL permit repeated pack IDs only when their effective aliases are unique for that pack and SHALL retain each instance's version request, destination, and remapping independently. Installed status, resolved versions, generated ownership, and content digests SHALL remain authoritative in `lunapack-lock.yml`.

#### Scenario: Read an existing unnamed requested root

- **WHEN** LunaPack reads a schema-valid requested pack entry without an alias
- **THEN** it interprets the entry as the pack-ID-named default instance without requiring an immediate rewrite

#### Scenario: Persist two named instances

- **WHEN** a user installs `orders` and `customers` instances of `dotnet-api` with distinct placements
- **THEN** `lunapack.yml` contains two `dotnet-api` entries with their respective aliases and placement settings

#### Scenario: Reject duplicate effective instance identities

- **WHEN** project configuration contains two requested pack entries with the same pack ID and effective alias
- **THEN** LunaPack reports invalid project state and performs no lifecycle mutation

#### Scenario: Preserve independent instance placement

- **WHEN** two entries for one pack declare different destinations or remaps
- **THEN** configuration reads and writes preserve each entry's placement settings without merging them
