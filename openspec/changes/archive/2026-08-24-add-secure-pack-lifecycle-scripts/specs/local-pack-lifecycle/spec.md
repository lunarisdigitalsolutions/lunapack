## ADDED Requirements

### Requirement: Authorize every lifecycle script before mutation

Before an install or update mutates managed files or project state, LunaPack SHALL build its complete applicable script plan for directly requested and transient packs and apply `--scripts <prompt|run|skip>`. The option SHALL default to `prompt`. `run` SHALL authorize every non-suppressed hook for that invocation without confirmation. `skip` SHALL execute no hooks and request no script confirmation. `prompt` SHALL authorize a hook only when its exact source identity is trusted, its source-identity-plus-pack-ID pair is trusted, or the user explicitly confirms it. Pack trust SHALL apply to every version of that ID from that exact source and SHALL not trust dependencies. Source trust SHALL apply only to packs resolved from that exact source identity.

For each script requiring consent, LunaPack SHALL show a structured confirmation containing the pack ID, hook type, optional description, and exact executable and arguments that will run. LunaPack SHALL fail closed without executing scripts or mutating that pack when consent is declined, input is unavailable, or authorization cannot be established. `--dry-run` SHALL execute no scripts and SHALL report each planned hook and whether it would require consent.

#### Scenario: Confirm an untrusted script

- **WHEN** an interactive install resolves an untrusted `preInstall` script
- **THEN** LunaPack shows its pack ID, hook, optional description, and exact command and executes it only after confirmation

#### Scenario: Decline before project mutation

- **WHEN** a user declines any script in a pack's lifecycle plan
- **THEN** LunaPack executes no script and makes no managed-file or state change for that pack

#### Scenario: Deny an untrusted script without interactive input

- **WHEN** an install or update cannot prompt and an applicable script is not otherwise trusted
- **THEN** LunaPack returns a non-success result without executing the script or mutating that pack

#### Scenario: Run all scripts for one invocation

- **WHEN** a user runs `luna install <pack-id> --scripts run` or `luna update <pack-id> --scripts run`
- **THEN** every non-suppressed script in that command is authorized without a confirmation prompt

#### Scenario: Skip all scripts for one invocation

- **WHEN** a user runs install or update with `--scripts skip`
- **THEN** LunaPack applies the pack lifecycle without executing or prompting for any script

#### Scenario: Trust one pack without trusting its dependency

- **WHEN** a trusted root pack resolves an untrusted dependency that declares a lifecycle script
- **THEN** LunaPack still requires authorization for the dependency script

#### Scenario: Trust a resolved source

- **WHEN** a pack script comes from an exact configured-source identity present in effective trust
- **THEN** LunaPack authorizes that script without prompting

#### Scenario: Do not trust a rebound source name

- **WHEN** a trusted source name is changed to identify another location
- **THEN** LunaPack requires authorization because the configured-source identity no longer matches

#### Scenario: Preview lifecycle scripts

- **WHEN** a user runs an install or update with `--dry-run`
- **THEN** LunaPack lists applicable hooks and consent requirements without executing any script or prompting for consent

### Requirement: Execute lifecycle scripts without an implicit shell

LunaPack SHALL start each authorized command as the declared executable with its declared arguments, and each authorized packed file as the declared runner with the confined file path followed by its declared arguments. It SHALL not pass either form through a command shell or interpret shell operators, substitutions, redirects, or environment expansion. It SHALL preserve each argument as one process argument. Before authorization, LunaPack SHALL materialize the resolved pack content into an operation snapshot and bind packed files to that snapshot. A packed file SHALL resolve within its snapshot root after canonicalization; missing files, rooted paths, and traversal outside the root SHALL fail before execution. No-follow traversal of links and reparse points is not part of this contract while ADR-0040 remains active.

#### Scenario: Pass metacharacters literally

- **WHEN** a hook argument contains characters that a command shell would interpret
- **THEN** LunaPack passes them as literal content in one process argument

#### Scenario: Invoke a file shipped in the pack

- **WHEN** a hook declares a script file beneath the resolved pack root and an explicit runner
- **THEN** LunaPack starts the runner with the canonical snapshotted file path as one argument

#### Scenario: Reject a packed script path escape

- **WHEN** a hook identifies a pack-relative script file that resolves outside the pack root through traversal
- **THEN** LunaPack returns a non-success result before executing any script or mutating the pack

#### Scenario: Execute the content that was authorized

- **WHEN** source content changes after LunaPack builds and displays the script plan
- **THEN** LunaPack executes the previously materialized snapshot rather than the changed source file

#### Scenario: Report process-start failure

- **WHEN** the declared executable cannot be started
- **THEN** LunaPack reports the hook and pack ID and returns a non-success result

### Requirement: Apply transient lifecycle suppression

Every resolved transient pack SHALL participate in lifecycle planning and execute the same install or update hooks as a directly requested pack unless its incoming composite references suppress those lifecycle types. Suppression SHALL be expressed by lifecycle type, not script name. When the same transient pack is reachable through multiple references, LunaPack SHALL suppress the union of every incoming `disabledHooks` collection. Suppression on a transient reference SHALL not suppress hooks when that pack is also a directly requested root.

#### Scenario: Execute hooks for a transient installation

- **WHEN** installing a composite root introduces a transient pack with install hooks and no suppression
- **THEN** LunaPack authorizes and executes the transient pack's install hooks in graph order

#### Scenario: Suppress selected transient hooks

- **WHEN** a composite reference disables `preInstall` and `postInstall` for its referenced transient pack
- **THEN** LunaPack executes neither install hook for that transient pack

#### Scenario: Apply the most restrictive shared policy

- **WHEN** a shared transient pack has multiple incoming references with different disabled hooks
- **THEN** LunaPack suppresses every lifecycle type disabled by any incoming reference

#### Scenario: Preserve directly requested root hooks

- **WHEN** a pack is both directly requested and referenced transitively with disabled hooks
- **THEN** LunaPack does not apply transient-reference suppression to its directly requested root lifecycle

### Requirement: Run lifecycle hooks in deterministic phases

For a resolved graph, LunaPack SHALL run applicable hooks in stable dependency-first order. A newly installed pack SHALL use `preInstall` before managed-file mutation and `postInstall` after managed-file mutation. An already installed pack moving to a different resolved release SHALL use the incoming release's `preUpdate` and `postUpdate` hooks around its managed-file mutation. A newly introduced dependency during update SHALL use install hooks. Unchanged and removed packs SHALL run none of these hooks.

LunaPack SHALL persist configuration, lock state, and resulting managed-file digests only after all applicable post hooks for the operation succeed. A pre-hook failure SHALL prevent managed-file and state mutation. A post-hook failure SHALL return a non-success result and restore LunaPack-managed files, configuration, and lock state to their pre-operation state. LunaPack SHALL report that external side effects created by a script cannot be rolled back.

#### Scenario: Install a composite graph in dependency order

- **WHEN** a root and its dependency both declare install hooks
- **THEN** LunaPack executes dependency `preInstall` before root `preInstall`, applies the planned graph mutation, then executes dependency `postInstall` before root `postInstall`

#### Scenario: Update with incoming hooks

- **WHEN** an installed pack updates to a release that declares update hooks
- **THEN** LunaPack executes that incoming release's `preUpdate`, applies its managed-file update, and then executes its `postUpdate`

#### Scenario: Install a new dependency during update

- **WHEN** an update introduces a dependency that was not previously installed
- **THEN** the new dependency runs its install hooks rather than update hooks

#### Scenario: Stop after a pre-hook failure

- **WHEN** a pre-install or pre-update process exits unsuccessfully
- **THEN** LunaPack stops the operation without changing managed files, configuration, or lock state

#### Scenario: Restore managed state after a post-hook failure

- **WHEN** a post-install or post-update process exits unsuccessfully
- **THEN** LunaPack restores managed files, configuration, and lock state, reports the failed hook, and warns that external script side effects may remain

### Requirement: Preserve project manifest integrity across every hook

LunaPack SHALL preserve a private backup and exact-byte digest of `lunapack.yml` before the first hook. It SHALL not reload project configuration from disk during hook execution. Immediately after every hook process exits, LunaPack SHALL verify that `lunapack.yml` still exists and has the same exact bytes. If it differs or is missing, LunaPack SHALL log an error identifying the pack and hook, restore the original manifest bytes, abort before another hook runs, and roll back LunaPack-owned managed files, configuration, and lock state. A script that changes and restores the same bytes before exit is outside this detection guarantee.

#### Scenario: Abort after a hook changes project configuration

- **WHEN** a lifecycle hook modifies `lunapack.yml` and exits
- **THEN** LunaPack restores the original bytes, logs an error, aborts immediately, and rolls back LunaPack-owned state

#### Scenario: Abort after a hook removes project configuration

- **WHEN** a lifecycle hook removes `lunapack.yml`
- **THEN** LunaPack restores the file and aborts before any later hook or state commit

### Requirement: Pin updates to locked source identity

LunaPack SHALL use each installed pack's locked configured-source identity when selecting update candidates for roots and transitive packs. An ordinary latest-version update SHALL not move a pack to another source. An explicit `luna update <pack-id>@<version>` MAY select that version from another configured source only when it is unavailable from the locked source. Before mutation or script authorization, LunaPack SHALL show the pack ID, old source identity, new source identity, and security consequence and require interactive source-switch confirmation. Declining or unavailable confirmation SHALL leave the graph unchanged. Trust for the old source or source-plus-pack pair SHALL not authorize scripts from the new source.

#### Scenario: Update from the locked source

- **WHEN** the locked source contains a newer eligible release
- **THEN** LunaPack selects that release without considering equal or newer candidates from other sources

#### Scenario: Refuse implicit source movement

- **WHEN** another source contains a newer release but the locked source does not
- **THEN** an ordinary update leaves the pack current at its locked-source release

#### Scenario: Confirm an explicit source switch

- **WHEN** an explicit requested version is unavailable from the locked source and available from another configured source
- **THEN** LunaPack shows both exact identities and switches only after the user confirms

#### Scenario: Deny a non-interactive source switch

- **WHEN** an explicit update would switch sources and interactive confirmation is unavailable
- **THEN** LunaPack returns a non-success result without changing files or state

#### Scenario: Reauthorize scripts after source switch

- **WHEN** a confirmed source switch selects a release with lifecycle hooks
- **THEN** LunaPack evaluates script trust against the new source identity
