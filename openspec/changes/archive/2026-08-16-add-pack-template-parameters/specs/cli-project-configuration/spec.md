## ADDED Requirements

### Requirement: Initialize and preserve project variables

LunaPack SHALL initialize `lunapack.yml` with an empty `variables` mapping and SHALL
preserve schema-valid project variables while reading and writing configuration
for lifecycle commands.

#### Scenario: Initialize a project with variables support

- **WHEN** a user runs `lunapack init` in an unconfigured directory
- **THEN** the created `lunapack.yml` contains an empty `variables` mapping

#### Scenario: Preserve configured variables during installation

- **WHEN** a project containing schema-valid variables installs a pack
- **THEN** LunaPack retains the variables in `lunapack.yml`
