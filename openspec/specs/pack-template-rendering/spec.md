# pack-template-rendering Specification

## Purpose

Allow pack authors to parameterize managed-file content and choose which files
an installation materializes without giving templates unrestricted lifecycle
control.

## Requirements

### Requirement: Render selected managed-file templates

LunaPack SHALL render every managed file selected for installation with the
resolved parameter values before it evaluates existing-target adoption, copies
content, or records a content digest. Templates SHALL use Scriban syntax and
receive the resolved parameter names as template variables. A template render
failure SHALL fail installation without changing project files or state.

#### Scenario: Render a string parameter into a managed file

- **WHEN** a pack template references `companyName` and installation resolves
  `companyName` to `Lunaris Digital Solutions`
- **THEN** the copied file contains `Lunaris Digital Solutions` at that
  template location

#### Scenario: Fail an invalid template without mutation

- **WHEN** a selected managed-file template cannot be rendered
- **THEN** LunaPack returns a non-success result and leaves project files,
  `lunapack.yml`, and `lunapack-lock.yml` unchanged

### Requirement: Conditionally select managed files

An optional managed-file `condition` SHALL select its file only when its
expression evaluates to true against resolved pack parameters. Conditions
SHALL support boolean parameter names and negation, equality and inequality
comparisons of string or enum parameters to string literals, and `&&`, `||`,
and parentheses for combining those operations. LunaPack SHALL reject a condition
that is syntactically invalid, references an undeclared parameter, or applies
an operator to an incompatible parameter type before mutation.

#### Scenario: Select a file for a true boolean condition

- **WHEN** a managed file has condition `includeCi` and `includeCi` resolves
  to true
- **THEN** LunaPack renders, copies, and records that managed file

#### Scenario: Omit a file for a false enum comparison

- **WHEN** a managed file has condition `licenseKind == "mit"` and
  `licenseKind` resolves to `apache-2.0`
- **THEN** LunaPack does not copy or record that managed file

#### Scenario: Reject an invalid condition

- **WHEN** a managed-file condition compares a boolean parameter to a string
  literal
- **THEN** LunaPack returns a non-success result before copying files or changing
  project state

### Requirement: Evaluate current time in templates

LunaPack SHALL make Scriban date-time functionality available while rendering
managed-file templates. The rendered content for a template that derives a
year from the current time SHALL contain the calendar year at installation.

#### Scenario: Render a current calendar year

- **WHEN** a selected template formats Scriban's current date-time value as a
  four-digit year
- **THEN** the copied content contains the year of the installation date

### Requirement: Render lifecycle instruction templates

LunaPack SHALL render each lifecycle instruction whose `templating` property is true as a strict Scriban template using the resolved parameters for that pack graph node. Instruction templates SHALL expose the same parameter names, values, and Scriban date-time functionality as managed-file templates. A template render failure or unknown variable SHALL fail lifecycle planning before any hook executes or project files or state change.

#### Scenario: Render a parameter into an instruction

- **WHEN** a templated instruction references a resolved `cloudProvider` parameter
- **THEN** the displayed instruction contains the resolved provider value

#### Scenario: Render current time in an instruction

- **WHEN** a templated instruction derives a year from Scriban's current date-time value
- **THEN** the displayed instruction contains the calendar year of the lifecycle operation

#### Scenario: Reject an unknown instruction parameter

- **WHEN** a templated instruction references an unknown parameter
- **THEN** LunaPack returns a non-success result before processing hooks or changing project files or state
