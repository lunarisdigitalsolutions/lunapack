## ADDED Requirements

### Requirement: Define remapping on composite references

Each composite pack reference in `pack.yml` SHALL allow an optional `remap` mapping with optional `directories` and `files` mappings equivalent to project pack remapping. Every key and non-`@ignore` value SHALL be a non-empty safe project-relative path. Existing composite references that omit `remap` SHALL remain valid.

#### Scenario: Validate composite reference remapping

- **WHEN** a composite reference declares valid file and directory remaps
- **THEN** the pack manifest is valid and preserves both mapping collections

#### Scenario: Validate a composite ignore mapping

- **WHEN** a composite reference maps a file or directory to the exact value `@ignore`
- **THEN** the pack manifest is valid and preserves the exclusion mapping

#### Scenario: Reject an unsafe composite remap

- **WHEN** a composite reference declares an empty, rooted, or escaping remap path
- **THEN** the pack manifest is invalid

### Requirement: Define expression bindings on composite references

Each composite reference parameter binding in `pack.yml` SHALL continue to
accept literal string, Boolean, and unique string-array values. It SHALL also
accept a string whose complete value has the form `${{ expression }}`. An
expression SHALL use the shared condition grammar and SHALL additionally allow
a parameter identifier as a typed value or
`iif(condition, whenTrue, whenFalse)` as a typed conditional value. String
literals inside `iif` SHALL remain quoted. Expression markers embedded within
other text SHALL remain literal strings.

Binding expressions SHALL reject lifecycle-only functions, undeclared source
parameters, incompatible branch types, cycles, unresolved values, and results
incompatible with the referenced parameter declaration.

#### Scenario: Validate a pass-through binding

- **WHEN** a composite reference binds a parameter to `${{ parentName }}` and
 the referencing pack declares `parentName`
- **THEN** the manifest accepts the binding expression

#### Scenario: Validate a conditional binding

- **WHEN** a composite reference binds a parameter to
 `${{ iif(isAngular, "angular", "react") }}`
- **THEN** the manifest accepts the typed conditional expression

#### Scenario: Preserve literal strings

- **WHEN** a composite reference binding contains text other than one complete
 `${{ expression }}` marker
- **THEN** LunaPack treats the complete value as a literal string

#### Scenario: Reject an invalid binding expression

- **WHEN** a binding expression references an undeclared parameter, uses a
 lifecycle-only function, contains incompatible `iif` branches, or forms a
 dependency cycle
- **THEN** LunaPack rejects the pack before project mutation

## MODIFIED Requirements

### Requirement: Define conditional pack declarations

The `pack.yml` schema SHALL allow an optional string `condition` on each managed-file and lifecycle-hook declaration. LunaPack SHALL evaluate both with the shared parameter expression grammar. That grammar SHALL support `isDefault(identifier)` for parameters with an explicit default, including negation and composition with existing logical operators. Lifecycle-hook conditions SHALL additionally support `scriptsSkipped()` as a boolean expression and `previousScriptState()` as a string-valued expression comparable with `succeeded`, `failed`, `skipped`, `cancelled`, `ignored`, and `none`. Runtime-state functions SHALL be invalid in managed-file conditions, composite-reference conditions, and parameter requiredness expressions. Existing manifests that omit `condition` SHALL remain valid.

#### Scenario: Validate a managed file without a condition

- **WHEN** schema validation receives an existing managed-file declaration without a condition
- **THEN** the pack manifest is valid

#### Scenario: Select a lifecycle instruction for a default value

- **WHEN** an instruction hook condition uses `isDefault(identifier)` and the resolved value equals that parameter's declared default
- **THEN** LunaPack includes the instruction in lifecycle planning

#### Scenario: Omit a lifecycle hook for an overridden default

- **WHEN** a lifecycle hook condition uses `isDefault(identifier)` and the consumer overrides that parameter
- **THEN** LunaPack omits the hook before instruction loading or script authorization

#### Scenario: Reject a default predicate without a default

- **WHEN** a condition uses `isDefault(identifier)` for a parameter without an explicit default
- **THEN** LunaPack rejects the condition before project mutation

#### Scenario: Validate runtime-state lifecycle conditions

- **WHEN** lifecycle hooks use `scriptsSkipped()` or compare `previousScriptState()` with a supported state
- **THEN** the pack manifest is valid

#### Scenario: Reject runtime state outside a lifecycle hook

- **WHEN** a managed file, composite reference, or parameter requiredness expression uses a runtime-state function
- **THEN** LunaPack rejects the declaration before project mutation

#### Scenario: Reject an unknown previous script state

- **WHEN** a lifecycle condition compares `previousScriptState()` with an unsupported state
- **THEN** LunaPack rejects the condition before project mutation
