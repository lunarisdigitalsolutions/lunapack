## ADDED Requirements

### Requirement: Inspect pack lifecycle scripts

`luna inspect <pack-id>[@<version>]` SHALL include a lifecycle scripts section when the resolved pack declares scripts. The section SHALL list hooks in `preInstall`, `postInstall`, `preUpdate`, and `postUpdate` order and show each hook type, optional description, and exact executable and ordered arguments. Inspection SHALL also list each composite reference's disabled lifecycle types. When a pack declares no scripts or a reference suppresses no hooks, inspection SHALL state that explicitly.

#### Scenario: Inspect every declared hook

- **WHEN** a resolved pack declares all four lifecycle hooks and the user runs `luna inspect` for that release
- **THEN** inspection lists all four hooks in lifecycle order with their descriptions and exact commands

#### Scenario: Inspect a pack without scripts

- **WHEN** a resolved pack declares no lifecycle scripts
- **THEN** inspection reports that the pack has no lifecycle scripts

#### Scenario: Inspect transient hook suppression

- **WHEN** a pack reference disables lifecycle hooks for its transient pack
- **THEN** inspection lists the referenced pack ID and each disabled lifecycle type
