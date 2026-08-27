# CLI Workflow Guidance Delta Specification

## ADDED Requirements

### Requirement: Guide external-source authoring and lifecycle outcomes

LunaPack SHALL provide no more than three contextually relevant next-step actions after pack initialization, external-source authoring, external-file authoring, source reuse, rejected source approval, successful install, successful update, and uninstall. Guidance SHALL use complete commands with known pack or source identifiers when available. Update guidance SHALL not recommend removal of sources made unused by the update. Uninstall MAY recommend `luna sources rm <source-id>` only when lock state shows no remaining pack or link consumer.

#### Scenario: Guide a newly initialized pack

- **WHEN** `luna pack init` succeeds
- **THEN** guidance includes commands to add managed content, add an external GitHub source, and validate the pack

#### Scenario: Guide an added pack source

- **WHEN** a pack-defined source is added successfully
- **THEN** guidance includes commands to add source-backed content and validate the pack

#### Scenario: Guide an unknown pack alias

- **WHEN** external-file authoring fails because the source alias is undeclared
- **THEN** guidance includes complete GitHub and Git source-add commands using the requested alias

#### Scenario: Guide rejected approval

- **WHEN** pack installation stops because required sources were not approved
- **THEN** guidance recommends inspecting the pack and adding the required source manually

#### Scenario: Avoid update cleanup guidance

- **WHEN** update succeeds and leaves an unconsumed configured source
- **THEN** guidance recommends lifecycle review actions without suggesting source removal

#### Scenario: Suggest safe uninstall cleanup

- **WHEN** uninstall succeeds and a workspace source has no remaining lock-file consumer
- **THEN** guidance may recommend removing that source explicitly
