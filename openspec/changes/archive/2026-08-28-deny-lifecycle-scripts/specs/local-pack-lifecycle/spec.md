# local-pack-lifecycle Delta Specification

## MODIFIED Requirements

### Requirement: Authorize every lifecycle script before mutation

Before an install, update, or uninstall mutates managed files or project state, LunaPack SHALL build its complete applicable script plan for directly requested and transient packs. It SHALL evaluate script denial from portable project trust, global-user trust, and project-local user trust before applying `--scripts <prompt|run|skip>` or any persisted source or pack grant. If any applicable scope has `deny.scripts: true`, LunaPack SHALL deny every non-suppressed script without prompting or resolving an executable, regardless of the supplied `--scripts` value. It SHALL retain instructions, continue the lifecycle without denied scripts, and emit a warning for every denied hook before processing any hook or mutation. Each warning SHALL identify the pack, hook, and every project, global-user, or project-local user scope causing denial.

When no denial applies, LunaPack SHALL apply `--scripts <prompt|run|skip>`. The option SHALL default to `prompt`. `run` SHALL authorize every non-suppressed hook for that invocation without confirmation. `skip` SHALL execute no hooks and request no script confirmation. `prompt` SHALL authorize a hook only when its exact source identity is trusted, its source-identity-plus-pack-ID pair is trusted, or the user explicitly confirms it. Pack trust SHALL apply to every version of that ID from that exact source and SHALL not trust dependencies. Source trust SHALL apply only to packs resolved from that exact source identity.

For each script requiring consent, LunaPack SHALL show a structured confirmation containing the pack ID, hook type, optional description, and exact command and arguments that will run. LunaPack SHALL fail closed without executing scripts or mutating the operation when consent is declined, input is unavailable, or authorization cannot be established. `--dry-run` SHALL execute no scripts or emit execution-denial warnings and SHALL report each planned hook with `policy-denied` plus every denying scope when denial applies, or its selected consent mode otherwise.

#### Scenario: Denial overrides invocation approval

- **WHEN** any applicable trust scope denies scripts and a user runs install, update, or uninstall with `--scripts run`
- **THEN** LunaPack requests no confirmation, executes no script, warns with the denying scope, and continues the lifecycle

#### Scenario: Denial overrides persisted grants

- **WHEN** a source or pack grant would authorize a lifecycle script but an applicable scope denies scripts
- **THEN** LunaPack retains the grant, denies the script without prompting, warns with every denying scope, and continues the lifecycle

#### Scenario: Preserve instructions when scripts are denied

- **WHEN** an ordered lifecycle plan contains a policy-denied script and an instruction that is not otherwise suppressed
- **THEN** LunaPack skips the script and processes the instruction in its declared lifecycle order

#### Scenario: Warn before mutation for each denied hook

- **WHEN** applicable pre- and post-lifecycle scripts are denied by policy
- **THEN** LunaPack emits one warning per denied hook identifying its pack, hook, and denying scopes before processing any hook or managed-file mutation

#### Scenario: Confirm an untrusted script

- **WHEN** no script denial applies and an interactive install resolves an untrusted `preInstall` script
- **THEN** LunaPack shows its pack ID, hook, optional description, and exact command and executes it only after confirmation

#### Scenario: Decline before project mutation

- **WHEN** no script denial applies and a user declines any script in a pack's lifecycle plan
- **THEN** LunaPack executes no script and makes no managed-file or state change for that operation

#### Scenario: Deny an untrusted script without interactive input

- **WHEN** no script denial applies and an install, update, or uninstall cannot prompt for an applicable script that is not otherwise trusted
- **THEN** LunaPack returns a non-success result without executing the script or mutating that operation

#### Scenario: Run all scripts for one invocation

- **WHEN** no script denial applies and a user runs install, update, or uninstall with `--scripts run`
- **THEN** every non-suppressed script in that command is authorized without a confirmation prompt

#### Scenario: Skip all scripts for one invocation

- **WHEN** no script denial applies and a user runs install, update, or uninstall with `--scripts skip`
- **THEN** LunaPack applies the pack lifecycle without executing or prompting for any script

#### Scenario: Trust one pack without trusting its dependency

- **WHEN** no script denial applies and a trusted root pack resolves an untrusted dependency that declares a lifecycle script
- **THEN** LunaPack still requires authorization for the dependency script

#### Scenario: Trust a resolved source

- **WHEN** no script denial applies and a pack script comes from an exact configured-source identity present in effective trust
- **THEN** LunaPack authorizes that script without prompting

#### Scenario: Do not trust a rebound source name

- **WHEN** no script denial applies and a trusted source name is changed to identify another location
- **THEN** LunaPack requires authorization because the configured-source identity no longer matches

#### Scenario: Preview lifecycle scripts

- **WHEN** a user runs an install, update, or uninstall dry run
- **THEN** LunaPack lists applicable hooks with policy-denial origins or consent modes without executing any script, prompting for consent, or emitting execution-denial warnings
