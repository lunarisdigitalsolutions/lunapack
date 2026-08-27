## ADDED Requirements

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
