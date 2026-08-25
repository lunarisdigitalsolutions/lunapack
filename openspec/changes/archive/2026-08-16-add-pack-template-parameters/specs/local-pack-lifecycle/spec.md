## ADDED Requirements

### Requirement: Accept typed installation parameters

`lunapack install` SHALL accept repeatable `--parameter <name>=<value>` input and
the `-p` alias. LunaPack SHALL resolve each supplied value against the declaration
for that name, accepting boolean values only for `bool` parameters and exact
allowed values only for `enum` parameters. It SHALL reject malformed entries,
unknown names, duplicate command-line names, and incompatible values before
changing project files or state.

#### Scenario: Supply a required string parameter

- **WHEN** a user runs `lunapack install license-mit -p companyName=Lunaris`
- **THEN** LunaPack resolves `companyName` as the string `Lunaris` for that
  installation

#### Scenario: Reject an invalid enum value

- **WHEN** a user supplies a value not declared by an enum parameter
- **THEN** LunaPack returns a non-success result without copying files or changing
  installation state

### Requirement: Resolve project variables for pack parameters

Before validating required parameters, LunaPack SHALL bind a project variable with
the same name as a declared graph parameter unless `--no-variables` is present
or that name is supplied by repeatable `--skip-variable <name>`. Explicit
`--parameter` values SHALL take precedence over eligible project variables.
Variables that cannot be converted to the declared parameter type SHALL fail
installation before mutation.

#### Scenario: Bind a matching project variable

- **WHEN** `lunapack.yml` defines `companyName` and the installed pack declares
  a required `companyName` string parameter without an explicit value
- **THEN** LunaPack uses the project variable to satisfy the parameter

#### Scenario: Skip a matching project variable

- **WHEN** a user passes `--skip-variable companyName` and provides no other
  value for a required `companyName` parameter
- **THEN** LunaPack returns a non-success result for the missing required
  parameter without mutation

### Requirement: Resolve a composite graph's compatible parameter set

LunaPack SHALL collect parameter declarations from every pack in a resolved
installation graph before it validates inputs or plans managed files. Same-name
declarations with the same type, requiredness, and enum values SHALL represent
one parameter; incompatible same-name declarations SHALL fail installation
without mutation. Every required graph parameter SHALL have a resolved value
from explicit input or an eligible project variable before installation begins.

#### Scenario: Satisfy shared composite parameter once

- **WHEN** two packs in a composite graph declare the same compatible required
  parameter and the user supplies it once
- **THEN** LunaPack uses that value for both packs

#### Scenario: Reject conflicting composite parameter declarations

- **WHEN** two packs in a resolved graph declare the same parameter name with
  incompatible types or enum values
- **THEN** LunaPack returns a non-success result before copying files or changing
  project state

### Requirement: Preserve rendered-file ownership semantics

LunaPack SHALL compute adoption comparisons and lock-file SHA-256 digests from
the rendered content of every condition-selected managed file. Files excluded
by a false condition SHALL not be copied or recorded. Existing preflight,
conflict detection, transaction rollback, and uninstall protections SHALL
apply to the rendered selected-file set.

#### Scenario: Adopt a rendered matching target

- **WHEN** `--adopt-existing` is used and an existing target matches its
  rendered template output
- **THEN** LunaPack records the target as managed without replacing its content

#### Scenario: Reject a rendered content mismatch

- **WHEN** an existing target differs from its rendered template output
- **THEN** LunaPack does not claim, replace, or record that target
