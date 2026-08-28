## MODIFIED Requirements

### Requirement: Accept typed installation parameters

`luna install` SHALL accept repeatable `--parameter <name>=<value>` input and
the `-p` alias. LunaPack SHALL resolve each supplied value against the
declaration for that name, accepting boolean values only for `bool` parameters
and exact allowed values only for `enum` parameters. Repeating one name SHALL
be valid only for a multi-select enum and SHALL produce a unique array in input
order. An optional multi-select enum with no higher-precedence input SHALL
resolve to an empty array. LunaPack SHALL reject malformed entries, unknown
names, duplicate scalar names, duplicate multi-select values, and incompatible
values before changing project files or state.

Optional parameters with declared defaults SHALL bind those defaults when no
higher-precedence value exists. Required parameters with defaults SHALL remain
interactive inputs and offer the default when prompting so accepting the prompt
uses that default. A required multi-select enum SHALL require an explicit,
variable, composite-binding, default, or prompted value source; an array source
MAY be empty.

#### Scenario: Supply a required string parameter

- **WHEN** a user runs `luna install license-mit -p companyName=Lunaris`
- **THEN** LunaPack resolves `companyName` as the string `Lunaris` for that
  installation

#### Scenario: Supply multiple enum selections

- **WHEN** a user supplies `-p features=api -p features=docker` for a
  multi-select enum
- **THEN** LunaPack resolves `features` as the array `["api", "docker"]`

#### Scenario: Resolve an omitted optional multi-select enum

- **WHEN** an optional multi-select enum has no explicit input, variable,
  composite binding, or default
- **THEN** LunaPack resolves it as an empty array

#### Scenario: Reject an invalid enum value

- **WHEN** a user supplies any value not declared by an enum parameter
- **THEN** LunaPack returns a non-success result without copying files or
  changing installation state

#### Scenario: Reject duplicate multi-select input

- **WHEN** a user supplies the same allowed value more than once for one
  multi-select enum
- **THEN** LunaPack returns a non-success result without copying files or
  changing installation state

#### Scenario: Accept a prompted parameter default

- **WHEN** a required parameter declares a valid default and the consumer
  accepts that default in the prompt
- **THEN** LunaPack resolves the declared default for that installation

### Requirement: Resolve project variables for pack parameters

Before validating required parameters, LunaPack SHALL bind a project variable
with the same name as a declared graph parameter unless `--no-variables` is
present or that name is supplied by repeatable `--skip-variable <name>`.
Explicit `--parameter` values SHALL take precedence over eligible project
variables. A project string array SHALL bind only to a multi-select enum and
SHALL preserve array order. Variables that cannot be converted to the declared
parameter type, contain duplicate selections, or contain a value outside the
enum's allowed set SHALL fail installation before mutation.

#### Scenario: Bind a matching project variable

- **WHEN** `lunapack.yml` defines `companyName` and the installed pack declares
  a required `companyName` string parameter without an explicit value
- **THEN** LunaPack uses the project variable to satisfy the parameter

#### Scenario: Bind a multi-select project variable

- **WHEN** `lunapack.yml` defines `features` as `[api, docker]` and the installed
  pack declares a compatible multi-select enum
- **THEN** LunaPack resolves `features` as `["api", "docker"]`

#### Scenario: Reject an invalid multi-select project variable

- **WHEN** a project variable array contains a duplicate or a value outside the
  multi-select enum declaration
- **THEN** LunaPack returns a non-success result without mutation

#### Scenario: Skip a matching project variable

- **WHEN** a user passes `--skip-variable companyName` and provides no other
  value for a required `companyName` parameter
- **THEN** LunaPack returns a non-success result for the missing required
  parameter without mutation

### Requirement: Resolve a composite graph's compatible parameter set

LunaPack SHALL collect parameter declarations from every pack in a resolved
installation graph before it validates inputs or plans managed files. For a
same-name declaration, the declaration nearest an installed root SHALL control
requiredness and enum values; all declarations SHALL retain the same type and
scalar-or-multi-select shape. Composite reference bindings SHALL supply
transient parameters when that name is not declared by an installed root, and
those values SHALL not be exposed to or overridden by consumer input. Every
remaining required graph parameter SHALL have a resolved value source from
explicit input, an eligible project variable, a composite binding, a default,
or a prompt before installation begins.

#### Scenario: Override a transient parameter declaration from the root

- **WHEN** a root and transient pack declare the same parameter with compatible
  types and scalar-or-multi-select shape but different requiredness or enum values
- **THEN** LunaPack uses the root declaration without merging enum values

#### Scenario: Bind a hidden transient parameter from a composite reference

- **WHEN** a composite reference supplies an allowed array for a multi-select
  enum declared only by its transient pack
- **THEN** LunaPack uses that array without exposing it to consumer input

#### Scenario: Reject a type-changing composite parameter override

- **WHEN** same-name declarations in a resolved graph use different types or
  differ between scalar and multi-select enum shape
- **THEN** LunaPack returns a non-success result before copying files or
  changing project state

### Requirement: Render lifecycle script arguments

LunaPack SHALL render each lifecycle script argument as a strict Scriban
template using the resolved graph parameters before dry-run formatting, trust
authorization, confirmation, or execution. Multi-select enum parameters SHALL
be exposed as arrays supporting the same membership behavior as managed-file
templates. Each rendered list item SHALL remain one process argument.
`command`, `runner`, and packed `file` values SHALL remain literal. An invalid
argument template or unknown variable SHALL fail planning before scripts
execute or project files or state change.

#### Scenario: Pass a parameter to a lifecycle script

- **WHEN** a script argument references a resolved pack parameter
- **THEN** dry-run, consent, and execution use the rendered value as one argument

#### Scenario: Test multi-select membership in a script argument

- **WHEN** a script argument template tests whether `features` contains
  `docker` and that value is selected
- **THEN** the rendered argument uses the matching branch

#### Scenario: Reject an unknown script parameter

- **WHEN** a script argument references an unknown parameter
- **THEN** LunaPack returns a non-success result before authorization or mutation
