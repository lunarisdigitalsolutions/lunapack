## ADDED Requirements

### Requirement: Define typed pack parameters

The `pack.yml` schema SHALL allow an optional `parameters` mapping keyed by a
non-empty parameter name. Each parameter declaration SHALL require a `type` of
`string`, `bool`, or `enum`; its `required` flag SHALL default to false. An
`enum` declaration SHALL contain a non-empty, unique collection of allowed
string `values`; other parameter types SHALL reject `values`. Existing valid
version-1 pack manifests without parameters SHALL remain valid.

#### Scenario: Validate an enum parameter declaration

- **WHEN** schema validation receives a parameter with type `enum`, a required
  flag, and distinct allowed string values
- **THEN** the pack manifest is valid

#### Scenario: Reject an unconstrained enum declaration

- **WHEN** schema validation receives an enum parameter without values or with
  duplicated values
- **THEN** the pack manifest is invalid

### Requirement: Define conditional managed files

The `pack.yml` schema SHALL allow an optional string `condition` on each
managed-file declaration. Existing manifests that omit `condition` SHALL
remain valid.

#### Scenario: Validate a managed file without a condition

- **WHEN** schema validation receives an existing managed-file declaration
  without a condition
- **THEN** the pack manifest is valid

### Requirement: Define project variables

The `lunapack.yml` schema SHALL allow an optional `variables` mapping whose
non-empty names map to string or boolean values. Existing valid version-1
project configuration without variables SHALL remain valid.

#### Scenario: Validate configured template variables

- **WHEN** schema validation receives project configuration with string and
  boolean variable values
- **THEN** the project manifest is valid

#### Scenario: Reject a non-scalar project variable

- **WHEN** schema validation receives a project variable whose value is not a
  string or boolean
- **THEN** the project manifest is invalid
