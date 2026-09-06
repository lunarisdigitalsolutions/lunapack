## ADDED Requirements

### Requirement: Author lifecycle runtime-state conditions

Hook add and replace commands SHALL accept schema-valid conditions containing `scriptsSkipped()` and `previousScriptState()`. Hook listing SHALL preserve and display the authored condition. Authoring validation SHALL reject unsupported state names and runtime-state functions outside lifecycle-hook conditions without partially changing `pack.yml`.

#### Scenario: Add a script-skip fallback instruction

- **WHEN** an author adds an instruction hook with condition `scriptsSkipped()`
- **THEN** LunaPack writes the condition to the new hook

#### Scenario: Add a previous-state condition

- **WHEN** an author adds a hook whose condition compares `previousScriptState()` with `ignored`
- **THEN** LunaPack writes a schema-valid condition and lists it with the hook

#### Scenario: Reject an unsupported state

- **WHEN** an author supplies a hook condition that compares `previousScriptState()` with an unsupported value
- **THEN** LunaPack reports the invalid condition and leaves `pack.yml` unchanged

### Requirement: Author composite parameter expression bindings

Composite reference add and replace commands SHALL preserve complete
`${{ expression }}` parameter values without evaluating or rewriting them.
Authoring validation SHALL parse the expression against parameters declared by
the referencing pack and SHALL reject malformed, undeclared, runtime-only, or
type-incompatible expressions without partially changing `pack.yml`. Reference
listing SHALL display the authored expression.

#### Scenario: Add a pass-through binding

- **WHEN** an author adds a reference parameter value `${{ parentName }}`
- **THEN** LunaPack writes and lists the expression unchanged

#### Scenario: Add a conditional binding

- **WHEN** an author adds
 `${{ iif(isAngular, "angular", "react") }}` as a reference parameter value
- **THEN** LunaPack writes a schema-valid expression binding

#### Scenario: Reject an invalid expression atomically

- **WHEN** an author adds or replaces a reference with an invalid expression
 binding
- **THEN** LunaPack reports the expression error and leaves `pack.yml` unchanged

## MODIFIED Requirements

### Requirement: Author composite references

The CLI SHALL let authors list, add, replace, and remove composite pack references. It SHALL support exact versions, literal and expression parameter bindings, disabled lifecycle hooks, and file and directory remaps accepted by the published schema. Listing SHALL display each binding and remap with its referenced pack context, and every mutation SHALL validate and replace the complete manifest atomically.

#### Scenario: Add a composite reference

- **WHEN** an author adds a pack ID with an exact version, parameter bindings, disabled hooks, and managed-target remaps
- **THEN** LunaPack writes one schema-valid composite reference

#### Scenario: Replace a composite reference

- **WHEN** an author sets an existing pack ID to a different exact version, bindings, hooks, or remaps
- **THEN** LunaPack replaces that reference without creating a duplicate ID

#### Scenario: List composite remaps

- **WHEN** an author lists a composite reference containing file and directory remaps
- **THEN** LunaPack displays each mapping with its kind, declared target, and effective target

#### Scenario: Remove a composite reference

- **WHEN** an author removes an existing composite pack ID
- **THEN** LunaPack removes only that reference

#### Scenario: Reject an invalid composite remap

- **WHEN** an author adds or replaces a composite reference with an unsafe or malformed remap
- **THEN** LunaPack reports the invalid input and leaves `pack.yml` unchanged
