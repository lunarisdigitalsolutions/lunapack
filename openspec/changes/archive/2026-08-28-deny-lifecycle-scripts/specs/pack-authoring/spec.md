# pack-authoring Delta Specification

## MODIFIED Requirements

### Requirement: Initialize a valid pack manifest

`luna pack init` SHALL create `pack.yml` in the selected workspace only after collecting every required manifest value. It SHALL accept `--id` and `--version`, default the version to `1.0.0`, prompt for missing required values only when an interactive terminal is available, and refuse to overwrite an existing manifest. The generated manifest SHALL pass the published pack schema, use canonical YAML property names, and contain only schema-required properties plus explicitly supplied non-default optional properties. It SHALL omit optional null, empty, and default-valued metadata, managed-file, composite-pack, parameter, source, tag, and lifecycle-hook properties. Required properties SHALL remain present even when their value came from an interactive or command default. Pack IDs SHALL use alphanumeric segments joined by single hyphens. The interactive license prompt SHALL default to `MIT`.

#### Scenario: Initialize from options

- **WHEN** an author runs `luna pack init --id dotnet-api --version 1.0.0` with required author and license input
- **THEN** LunaPack creates a valid `pack.yml` without prompting and omits every optional empty or default-valued property

#### Scenario: Initialize interactively

- **WHEN** an author runs `luna pack init` in an interactive terminal
- **THEN** LunaPack prompts for the missing pack ID and required attribution, offers `1.0.0` as the version default and `MIT` as the license default, and writes only required properties after the resulting manifest validates

#### Scenario: Retain required defaulted values

- **WHEN** initialization uses the default version or license value for a schema-required property
- **THEN** the generated manifest includes that required property while omitting optional defaults

#### Scenario: Reject an invalid pack ID

- **WHEN** an author supplies a pack ID with any non-alphanumeric, non-hyphen character, repeated hyphens, or leading or trailing hyphen
- **THEN** LunaPack rejects the manifest and preserves any existing file

#### Scenario: Reject missing non-interactive input

- **WHEN** required initialization input is missing and no interactive terminal is available
- **THEN** LunaPack reports each missing option and does not create `pack.yml`

#### Scenario: Preserve an existing manifest

- **WHEN** `pack.yml` already exists in the selected workspace
- **THEN** initialization fails without changing the file
