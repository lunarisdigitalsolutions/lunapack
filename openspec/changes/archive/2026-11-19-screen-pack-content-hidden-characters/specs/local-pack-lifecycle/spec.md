# Spec Delta

## ADDED Requirements

### Requirement: Screen managed text before lifecycle changes

`luna install` and `luna update` SHALL screen eligible final managed-file content planned for creation or replacement after template rendering and merge planning, before hooks execute or project files or state change. Findings SHALL identify the effective project-relative target.

#### Scenario: Report a rendered install finding

- **WHEN** an install renders a managed template containing a hidden character for effective target `docs/readme.md`
- **THEN** LunaPack reports the finding against `docs/readme.md` before applying the plan

#### Scenario: Screen merged update content

- **WHEN** an update plans a merge whose resulting text contains a hidden character
- **THEN** LunaPack reports the finding in the final planned target content

#### Scenario: Screen a dry-run plan

- **WHEN** an install or update dry run plans eligible text containing a hidden character
- **THEN** LunaPack reports the same finding without changing project files or state

#### Scenario: Block before lifecycle side effects

- **WHEN** enforcement is enabled and a lifecycle plan contains a high-confidence finding without an override
- **THEN** LunaPack stops before executing hooks or changing project files, configuration, or lock state

### Requirement: Screen installed managed text during audit

`luna audit` SHALL screen existing eligible managed-file targets and report findings with their installed project-relative paths. Findings SHALL not change audit exit status or mutate project files, configuration, or lock state.

#### Scenario: Report a finding in an installed file

- **WHEN** an existing installed managed-file target contains a hidden character
- **THEN** `luna audit` reports the finding against that target

#### Scenario: Preserve audit state

- **WHEN** `luna audit` reports hidden-character findings
- **THEN** it leaves all files and project state unchanged and does not fail solely because findings exist
