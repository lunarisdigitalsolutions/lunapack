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

### Requirement: Resolve managed-file targets in templates

LunaPack SHALL expose `files.path(target)` and `files.relative_path(target)` to
managed-file Scriban templates. Each function SHALL look up `target` by a
managed file's manifest-declared target in the resolved installation plan.
`files.path` SHALL return the selected file's effective project-relative target.
`files.relative_path` SHALL return the relative path from the current template
file's effective target directory to the selected file's effective target.
Both functions SHALL use the effective targets after remapping, SHALL return
paths with `/` separators on every platform, and SHALL behave identically while
planning installation, update, and dry-run operations. The functions SHALL
expose no filesystem discovery, reading, writing, or existence checks to the
template.

#### Scenario: Resolve a remapped managed-file target

- **WHEN** a template calls `files.path` with declared target
  `docs/development/code-review.md` and the resolved installation plan remaps
  that file to `docs/04-development/process/code-review.md`
- **THEN** the function returns
  `docs/04-development/process/code-review.md`

#### Scenario: Calculate a relative path from effective targets

- **WHEN** a template calls `files.relative_path` for a selected managed file
  and both files have effective targets changed by remapping
- **THEN** the function returns the `/`-separated lexical relative path from the
  current template file's effective target directory to the referenced file's
  effective target

#### Scenario: Preserve resolution across lifecycle planning modes

- **WHEN** the same resolved graph, parameters, remapping, and lock state are
  planned for installation, update, and dry-run operations
- **THEN** each operation renders the same values from `files.path` and
  `files.relative_path`

### Requirement: Preserve unresolved managed-file references

When either managed-file path function cannot identify exactly one selected
managed file by the supplied declared target, LunaPack SHALL emit a warning that
identifies the unresolved declared target and the current template's effective
target. Rendering SHALL continue, and the function SHALL return the supplied
declared target unchanged. A managed file excluded by its condition SHALL be
unavailable to both functions and SHALL use the same warning and fallback
behavior. These warnings SHALL not make installation, update, or dry-run
planning fail.

#### Scenario: Preserve a missing declared target

- **WHEN** a managed-file template references a declared target absent from the
  resolved installation plan
- **THEN** LunaPack warns that the target could not be resolved while rendering
  the current effective target and renders the original declared target
  unchanged

#### Scenario: Preserve a conditionally excluded target

- **WHEN** a managed-file template references a managed file whose condition
  excludes it from the resolved installation plan
- **THEN** LunaPack emits the unresolved-target warning and renders the original
  declared target unchanged without failing the operation

#### Scenario: Preserve an ambiguous declared target

- **WHEN** more than one selected managed file has the referenced declared
  target
- **THEN** LunaPack treats the reference as unresolved, emits the warning, and
  renders the original declared target unchanged
