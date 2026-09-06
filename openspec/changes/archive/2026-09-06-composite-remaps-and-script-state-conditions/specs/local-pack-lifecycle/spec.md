## ADDED Requirements

### Requirement: Evaluate composite parameter binding expressions

LunaPack SHALL evaluate an active composite reference's `${{ expression }}`
parameter bindings against resolved parameters declared by the referencing
pack. A direct identifier SHALL preserve its resolved string, Boolean, enum, or
multi-select value. `iif(condition, whenTrue, whenFalse)` SHALL evaluate its
condition with the shared parameter condition grammar and return only the
selected typed branch. Nested `iif` calls and parameter identifiers SHALL be
valid branch values.

Expression bindings SHALL be resolved before prompting for the referenced
transient parameter and SHALL remain fixed composite values under existing
binding precedence. LunaPack SHALL resolve expression dependencies in a
deterministic order independent of manifest declaration order. Unknown,
unresolved, cyclic, runtime-dependent, or target-incompatible expressions SHALL
fail preflight.

#### Scenario: Pass through a renamed parameter

- **WHEN** a referencing pack resolves `frameworkName` and binds a referenced
 parameter to `${{ frameworkName }}`
- **THEN** the referenced parameter receives the same typed value without a
 second prompt

#### Scenario: Select a value from a condition

- **WHEN** `isAngular` resolves true and a referenced parameter is bound to
 `${{ iif(isAngular, "angular", "react") }}`
- **THEN** the referenced parameter resolves to `angular`

#### Scenario: Preserve a multi-select value

- **WHEN** a direct identifier expression references a resolved multi-select
 parameter compatible with the referenced declaration
- **THEN** the referenced parameter receives the ordered selection unchanged

#### Scenario: Reject a binding cycle

- **WHEN** active expression bindings depend on one another cyclically
- **THEN** LunaPack returns a non-success result before prompting or mutation

### Requirement: Apply composite-reference remapping to dependency subtrees

LunaPack SHALL apply file and directory remaps declared on an active composite reference to managed files owned by the referenced pack and every pack reachable through that reference. A remap declared on a nearer active reference SHALL override an overlapping remap inherited from an ancestor reference. Command-line remaps SHALL take precedence over requested-root project remaps, which SHALL take precedence over top-level project remaps, which SHALL take precedence over composite-reference remaps. Exact file mappings SHALL retain precedence over directory mappings within each source. Conflicting mappings at the same graph depth for a pack reached through multiple active references SHALL fail preflight. Effective targets, `@ignore`, lock retention, ownership checks, and dry-run reporting SHALL otherwise follow existing remapping behavior, and reported remaps SHALL identify their composite pack and reference origin.

#### Scenario: Remap a referenced pack target

- **WHEN** a composite reference remaps a directory managed by its referenced pack
- **THEN** LunaPack installs the referenced pack's matching files beneath the remapped directory

#### Scenario: Cascade a remap through descendants

- **WHEN** an active composite reference remaps a target also managed by a transitive descendant
- **THEN** LunaPack applies that remap to the descendant unless a nearer reference overrides it

#### Scenario: Prefer the nearest composite remap

- **WHEN** an ancestor and a nearer composite reference declare overlapping remaps for a descendant
- **THEN** LunaPack uses the nearer reference's mapping

#### Scenario: Preserve consumer remap authority

- **WHEN** a command-line, requested-root, or top-level project remap overlaps a composite-reference remap
- **THEN** LunaPack uses the highest-precedence consumer remap

#### Scenario: Reject ambiguous same-depth remaps

- **WHEN** a pack is reached through multiple active references at the same depth with conflicting applicable remaps
- **THEN** LunaPack returns a non-success result before changing project files or state

#### Scenario: Ignore a composite subtree target

- **WHEN** a composite reference maps a target to `@ignore`
- **THEN** LunaPack omits matching files in that reference's dependency subtree unless a higher-precedence mapping retains them

### Requirement: Evaluate lifecycle runtime-state conditions

LunaPack SHALL evaluate `scriptsSkipped()` and `previousScriptState()` only for lifecycle-hook conditions. `scriptsSkipped()` SHALL be true when scripts are suppressed for the whole invocation by the effective script mode or an applicable trust-policy denial, and false when scripts are merely absent, disabled, excluded by individual conditions, or declined individually. `previousScriptState()` SHALL inspect the nearest earlier script declaration in the same pack and lifecycle event and SHALL return `none` when none exists, `ignored` when its condition was false, `skipped` when whole-invocation suppression or an individual authorization decline prevented execution, `cancelled` when execution was cancelled, `succeeded` after successful execution, or `failed` after unsuccessful execution. State SHALL not cross pack or lifecycle-event boundaries.

LunaPack SHALL preserve manifest order while evaluating runtime-state conditions. It SHALL authorize every script that may become applicable before processing the first hook, then evaluate runtime-dependent inclusion when each declaration is reached. A failed or cancelled script SHALL retain existing abort and rollback behavior, so no later hook is processed. A dry run SHALL validate runtime-dependent declarations and report them as conditional without assuming a previous execution result.

#### Scenario: Show instructions when scripts are globally skipped

- **WHEN** an instruction condition is `scriptsSkipped()` and scripts are suppressed by effective script mode or trust policy
- **THEN** LunaPack processes the instruction without executing a script

#### Scenario: Do not infer global skip from an ignored script

- **WHEN** every script declaration is excluded by its own false condition and no invocation-wide suppression applies
- **THEN** `scriptsSkipped()` evaluates to false

#### Scenario: React to an ignored previous script

- **WHEN** a script condition is false and the following instruction condition compares `previousScriptState()` with `ignored`
- **THEN** LunaPack processes the instruction

#### Scenario: React to an individually skipped script

- **WHEN** a user declines an individual script and the following instruction condition compares `previousScriptState()` with `skipped`
- **THEN** LunaPack processes the instruction while `scriptsSkipped()` remains false

#### Scenario: Return none before any script

- **WHEN** a hook condition calls `previousScriptState()` before any earlier script declaration in the same pack and event
- **THEN** the function returns `none`

#### Scenario: Keep state local to an event

- **WHEN** a pack begins a lifecycle event after a script ran in another pack or event
- **THEN** `previousScriptState()` returns `none` until a script declaration is reached in the current pack and event

#### Scenario: Authorize a runtime-dependent script before processing

- **WHEN** a script condition depends on `previousScriptState()`
- **THEN** LunaPack obtains any required authorization before processing the event and executes the script only if its condition is true when reached

#### Scenario: Preserve abort after script failure

- **WHEN** an executed script fails and a later hook tests for `failed`
- **THEN** LunaPack aborts and applies existing rollback behavior without processing the later hook

#### Scenario: Preview a runtime-dependent hook

- **WHEN** a dry run encounters a hook whose condition depends on `previousScriptState()`
- **THEN** LunaPack validates and reports the hook as runtime-conditional without claiming it will run
