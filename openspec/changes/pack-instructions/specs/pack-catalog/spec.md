## ADDED Requirements

### Requirement: Inspect ordered lifecycle hooks

`luna inspect <pack-id>[@<version>]` SHALL include a lifecycle hooks section when the resolved pack declares hooks. The section SHALL list events in `preInstall`, `postInstall`, `preUpdate`, and `postUpdate` order and list each event's hooks in declaration order. For a script hook, inspection SHALL show its optional description and exact executable and ordered arguments. For an instruction hook, inspection SHALL show its pack-relative file and effective templating state. Inspection SHALL also list each composite reference's disabled lifecycle events. When a pack declares no hooks or a reference suppresses no events, inspection SHALL state that explicitly.

#### Scenario: Inspect mixed ordered hooks

- **WHEN** a resolved pack event declares instruction and script hooks and the user inspects that release
- **THEN** inspection lists both typed hooks in declaration order with their type-specific properties

#### Scenario: Inspect a pack without hooks

- **WHEN** a resolved pack declares no lifecycle hooks
- **THEN** inspection reports that the pack has no lifecycle hooks

#### Scenario: Inspect transient hook suppression

- **WHEN** a pack reference disables lifecycle events for its transient pack
- **THEN** inspection lists the referenced pack ID and each disabled lifecycle event

## REMOVED Requirements

### Requirement: Inspect pack lifecycle scripts

**Reason**: Script-only inspection cannot represent unified ordered script and instruction hooks.

**Migration**: Use the lifecycle hooks section, which retains script execution details and adds instruction metadata in declaration order.
