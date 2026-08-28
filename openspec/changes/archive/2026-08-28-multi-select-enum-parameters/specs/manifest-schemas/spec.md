## MODIFIED Requirements

### Requirement: Define typed pack parameters

The `pack.yml` schema SHALL allow an optional `parameters` mapping keyed by a
non-empty parameter name. Each parameter declaration SHALL require a `type` of
`string`, `bool`, or `enum`; its `required` flag SHALL default to false. An
`enum` declaration SHALL contain a non-empty, unique collection of allowed
string `values` and MAY set `multiple` to true; other parameter types SHALL
reject `values` and `multiple`. An omitted `multiple` property SHALL be
equivalent to false. A parameter MAY declare non-empty `displayName` and
`description` strings for interactive prompts. A parameter MAY define a
`default` matching its declared type. A scalar enum default SHALL be one of its
declared values. A multi-select enum default SHALL be a unique array containing
zero or more declared values. Existing valid version-1 pack manifests without
parameters or `multiple` SHALL remain valid.

#### Scenario: Validate an enum parameter declaration

- **WHEN** schema validation receives a parameter with type `enum`, a required
  flag, and distinct allowed string values
- **THEN** the pack manifest is valid

#### Scenario: Validate a multi-select enum declaration

- **WHEN** schema validation receives an enum parameter with `multiple: true`
  and distinct allowed string values
- **THEN** the pack manifest is valid

#### Scenario: Reject multiple on another parameter type

- **WHEN** schema validation receives a string or boolean parameter with a
  `multiple` property
- **THEN** the pack manifest is invalid

#### Scenario: Validate parameter display metadata

- **WHEN** a parameter declaration contains display name and description text
- **THEN** the pack manifest is valid

#### Scenario: Reject an unconstrained enum declaration

- **WHEN** schema validation receives an enum parameter without values or with
  duplicated values
- **THEN** the pack manifest is invalid

#### Scenario: Validate a typed parameter default

- **WHEN** a string or boolean parameter declares a default of the matching type
- **THEN** the pack manifest is valid

#### Scenario: Validate a multi-select enum default

- **WHEN** a multi-select enum default is an empty array or a unique array of
  values from its allowed set
- **THEN** the pack manifest is valid

#### Scenario: Reject an invalid enum default

- **WHEN** an enum default has the wrong scalar-or-array shape, contains a
  duplicate, or contains a value outside its declared values
- **THEN** the pack manifest is invalid

### Requirement: Define project variables

The `lunapack.yml` schema SHALL allow an optional `variables` mapping whose
non-empty names map to string values, boolean values, or unique arrays of
strings. Arrays SHALL preserve their declared order and provide values for
multi-select enum parameters. Existing valid version-1 project configuration
without variables or array values SHALL remain valid.

#### Scenario: Validate configured template variables

- **WHEN** schema validation receives project configuration with string,
  boolean, and unique string-array variable values
- **THEN** the project manifest is valid

#### Scenario: Reject a non-scalar project variable

- **WHEN** schema validation receives a project variable whose value is not a
  string, boolean, or unique string array
- **THEN** the project manifest is invalid

## ADDED Requirements

### Requirement: Define multi-select composite parameter bindings

The `pack.yml` schema SHALL allow a composite reference parameter binding to
contain a unique array of strings for a referenced multi-select enum parameter.
The schema SHALL preserve existing string and boolean binding values, and
runtime validation SHALL reject an array binding for any non-multi-select
parameter or any selected value outside the referenced declaration.

#### Scenario: Validate a multi-select composite binding

- **WHEN** a composite reference binds a unique string array to a multi-select
  enum declared by its referenced pack
- **THEN** the pack manifest and runtime binding are valid

#### Scenario: Reject an incompatible composite binding

- **WHEN** a composite reference binds an array to a scalar parameter or binds
  a value outside the referenced enum declaration
- **THEN** LunaPack rejects the pack before changing project files or state
