# CLI Project Configuration Delta Specification

## MODIFIED Requirements

### Requirement: Initialize a LunaPack project manifest

The `luna init` command SHALL create `lunapack.yml` and `lunapack-lock.yml` in the current directory when neither exists. The configuration SHALL conform to the project-configuration schema with schema version `1`, empty `sources`, `packs`, `links`, and `variables` collections, and a `trust` mapping containing empty `sources` and `packs` collections. The lock file SHALL conform to its schema with an empty resolved pack graph and empty resolved link collection.

#### Scenario: Initialize an unconfigured directory

- **WHEN** a user runs `luna init` in a directory without `lunapack.yml` or `lunapack-lock.yml`
- **THEN** LunaPack creates schema-valid empty configuration and lock files, including empty link, source, pack, and trust collections

#### Scenario: Refuse to replace an existing manifest

- **WHEN** a user runs `luna init` in a directory that already contains `lunapack.yml` or `lunapack-lock.yml`
- **THEN** LunaPack leaves existing project state unchanged and returns a non-success result

## ADDED Requirements

### Requirement: Persist portable link definitions

`lunapack.yml` SHALL contain link definitions as project-owned intent separate from resolved source commits, selected-file inventories, ownership, and content digests. Commands that read and write project configuration SHALL preserve schema-valid links they do not modify. Existing version-1 configurations that omit `links` SHALL remain valid and SHALL be interpreted as having no links.

#### Scenario: Preserve links during an unrelated configuration change

- **WHEN** a user modifies a source, remapping, variable, trust entry, or requested pack through the CLI
- **THEN** LunaPack preserves every schema-valid link definition in `lunapack.yml`

#### Scenario: Read existing configuration without links

- **WHEN** LunaPack reads a valid version-1 project configuration that omits `links`
- **THEN** it treats the project as having no configured links without requiring migration
