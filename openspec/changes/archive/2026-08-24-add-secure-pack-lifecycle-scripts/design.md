## Context

See [proposal.md](proposal.md) for motivation. Pack manifests are parsed into catalog candidates, resolved into dependency graphs, materialized by source-specific providers, and then applied by separate install and update services. Project configuration currently identifies sources only by location, while script trust needs a stable user-facing source identity that survives catalog resolution.

Hook execution crosses manifest, catalog, lifecycle, process, CLI, schema, and documentation boundaries. Consent must bind to the content and argv that actually execute. LunaPack can restore files and YAML state under its control, but it cannot reverse arbitrary external effects from a child process.

## Goals / Non-Goals

**Goals:**

- Make the packed-file form distinguishable from a direct command so path confinement is enforceable.
- Apply one fail-closed authorization policy to install and update graphs.
- Bind consent to an immutable materialized snapshot and an unambiguous argv display.
- Execute and suppress transient lifecycle hooks deterministically.
- Pin updates and pack trust to normalized configured-source identity.
- Preserve existing graph preflight and managed-file rollback behavior while preventing implicit source movement.
- Keep source and pack trust narrow, explicit, auditable, user-acknowledged, and version-independent.

**Non-Goals:**

- Provide an operating-system sandbox, privilege reduction, or container boundary.
- Make arbitrary script side effects transactional.
- Add uninstall hooks, multiple scripts per hook, hook conditions, environment declarations, or script output parsing.
- Persist the one-invocation `--scripts` decision.
- Guarantee that authorized child processes cannot affect resources outside the project.
- Detect a script that changes `lunapack.yml` and restores identical bytes before process exit.
- Add a new project or lock schema version.

## Decisions

### Use a tagged union for each hook

`PackManifest` gains an optional `Scripts` object with `PreInstall`, `PostInstall`, `PreUpdate`, and `PostUpdate` members. Each hook uses exactly one of these YAML shapes:

```yaml
scripts:
  preInstall:
    file: scripts/setup.ps1
    runner: pwsh
    arguments:
      - -ProjectType
      - library
    description: Configure project tooling.
  postInstall:
    command: dotnet
    arguments:
      - tool
      - restore
```

The packed-file form requires `file` and `runner`; the command form requires `command`. Both support ordered `arguments` and an optional description. The executor constructs argv as `<runner> <canonical-file> <arguments...>` or `<command> <arguments...>`.

Each composite reference may also declare `disabledHooks` as a set of lifecycle types. These values control the referenced transient pack as a unit; hooks have no author-controlled names to target individually.

This keeps file paths explicit and enforceable while allowing a command that needs no shipped file. A free-form shell string was rejected because quoting is platform-dependent and shell parsing expands the injection surface. Inferring a runner from a file extension was rejected because file associations differ by platform and can invoke unexpected handlers.

### Snapshot resolved pack content before authorization

Source providers materialize every resolved pack into a private operation staging directory before hook authorization. Manifest validation, managed-file planning, packed-file canonicalization, and hook display all use that snapshot. Install/update file content and packed hooks then execute from the same snapshot. The operation removes staging content on completion or failure.

Snapshot materialization currently copies source entries into a private operation
directory, but follows symbolic links, junctions, mount-point redirects, and
other reparse points. It is therefore not a no-follow confinement boundary;
ADR-0040 records this deferred control and residual risk. Packed-file paths use
`ProjectPath` followed by filesystem canonicalization; the final target must
remain beneath the snapshot root after copying.

The planner hashes every packed hook file and stores the digest with the displayed invocation. The executor verifies that digest immediately before launch and aborts if an earlier hook or another process changed staged content. Digest verification binds the copied content, not the source-tree traversal path.

Copying local packs as well as Git packs closes the source time-of-check/time-of-use window. Executing directly from a mutable local source was rejected because content could change after the user confirms it.

### Use normalized configured-source identities

Every project source gains a required ordinal-unique `Name`. The security identity is not the name alone. A local configured-source identity in the portable lock contains its type and normalized project-relative configured path; user trust resolves that value to the canonical physical directory for the current project. A Git source identity contains its type, canonical repository URL, normalized ref, and normalized repository path. Git resolved commit remains release provenance but is excluded from source identity so later commits from the same configured source can update normally.

`ConfiguredSource`, catalog candidates, resolved graph nodes, lifecycle plans, and lock records carry source name plus normalized identity. Source names remain presentation and lookup keys. Source precedence remains configuration order for installation and explicit source-switch candidate selection.

Every resolved root and transient lock record persists the configured-source identity and Git commit when applicable. Ordinary update resolution starts from each node's locked identity and does not consider other sources. For explicit `pack-id@version`, the locked source remains preferred. If it cannot provide the version, the normal configured precedence selects another source, then a dedicated confirmation shows both identities before any script authorization or mutation. Trust is reevaluated against the new source.

Allowing ordinary latest updates to roam across sources was rejected because equal pack IDs do not establish publisher continuity.

### Store trust in user, project, and global scopes

`~/.lunapack/config.yml` is resolved from the operating system's user-profile directory, not the process working directory. It stores global-user trust and local-user trust keyed by canonical physical project directory. LunaPack creates the directory and file with owner-only permissions, rejects links or reparse points in the settings path, and updates settings through same-directory atomic replacement.

Project trust remains declarative in `lunapack.yml`, but it cannot self-authorize. A confirmed `--project` trust command writes both the declaration and a user-local acknowledgement containing canonical project path and exact source identities. Project declarations are effective only while that acknowledgement matches. Cloning a repository or accepting a malicious change to `lunapack.yml` therefore cannot silently grant execution.

The trust commands use these mutually exclusive scopes:

- No scope option: local-user trust for the canonical current project, stored in user settings.
- `--project`: project declaration in `lunapack.yml` plus current-user acknowledgement in user settings.
- `--global`: user-wide trust stored in user settings.

Pack trust is always `(source identity, pack ID)`, never ID alone. `luna trust pack <id>... --source <name>` resolves the name before persistence. Source trust stores exact normalized identities. Trust evaluation unions effective scopes and uses ordinal identity values:

1. `--scripts run` authorizes every non-suppressed hook in the invocation.
2. Exact source-identity-plus-pack-ID trust authorizes hooks for that pair regardless of version.
3. Exact source identity trust authorizes hooks from that source.
4. In `prompt` mode, every remaining hook requires individual confirmation.
5. `--scripts skip` suppresses all hooks before trust evaluation.

Trust is evaluated per resolved graph node, so trusting a composite root does not trust transitive packs. The `luna trust` command validates all input first and saves once, preventing partial changes. Repeating an existing value is idempotent.

Before a trust save, one shared formatter displays a danger panel covering source/repository compromise, future versions, dependency changes, inherited user permissions, accessible credentials, filesystem/network access, and irreversible effects. It lists exact scope and normalized identities. Trust creation has no non-interactive bypass.

Storing broad booleans, trusting by source name, trusting an ID across publishers, or trusting a root's dependency graph was rejected because each silently expands authority.

### Plan transient hooks and suppression over the resolved graph

Every new resolved graph node receives install hooks, including transient nodes. Every existing node moving to another release receives the incoming release's update hooks. Unchanged and removed nodes receive none.

For each transient node, the planner unions `disabledHooks` from all incoming composite references. This most-restrictive result avoids execution depending on graph traversal order. A node also installed as a directly requested root retains its direct-root hooks; reference suppression controls only its transient role.

Suppression is calculated before authorization so users are never prompted for a hook that cannot execute. `luna inspect` shows declarations on each reference, while dry-run shows the effective graph policy.

### Separate planning, authorization, and execution

A shared lifecycle-script planner maps graph differences and transient suppression to immutable hook invocations. Stable dependency-first graph order controls each phase.

The lifecycle flow becomes:

1. Load and validate project state.
2. Resolve and snapshot the complete graph.
3. Preflight managed files and build the complete hook plan.
4. Apply `--scripts`, effective trust, and explicit confirmation to every non-suppressed hook.
5. Run pre hooks in dependency-first order.
6. Apply the managed-file transaction.
7. Run post hooks in dependency-first order.
8. Recompute resulting managed-file digests and commit configuration and source-pinned lock state.

Authorization completes before the first hook or managed-file mutation. `--scripts` defaults to `prompt`; `run` grants only the current invocation and `skip` executes no hook while still applying declarative file changes. Dry runs stop after planning and render file actions, effective suppression, source switches, and hook authorization status. Non-interactive input fails closed when any hook or source switch still needs confirmation.

Prompting during a post phase was rejected because declining then would occur after project mutation. Using the installed release's `preUpdate` was rejected because inspection and consent must describe the incoming content that the update will execute.

### Execute argv directly and expose process output

A dedicated executor receives an already authorized immutable invocation. Before display, it resolves an external command or runner through the current executable search path to one canonical executable path; confirmation and execution use that same path. It uses direct process start with shell execution disabled, sets the project directory as the working directory, supplies the canonical snapshot file as one argument for packed-file hooks, and appends each manifest argument without concatenation or expansion. It streams stdout and stderr with pack/hook context after removing unsafe terminal control sequences and treats start failure, cancellation, or nonzero exit as hook failure. Cancellation terminates the child process tree.

Confirmation and inspection use one formatter that shows hook, pack ID and version, source name, description when present, executable, and arguments as separately escaped values. Packed-file confirmation also shows its pack-relative path; random staging paths remain an implementation detail.

LunaPack does not claim process isolation. The confirmation text and developer documentation state that an authorized process runs with the user's permissions and may affect files or services outside LunaPack's transaction.

### Guard `lunapack.yml` across each process boundary

Before the first hook, LunaPack stores the exact manifest bytes, digest, and existence state in transaction backup. Lifecycle code retains its already parsed immutable configuration and never reloads project configuration while hooks run. After each child exits, before another hook or state transition, LunaPack reads `lunapack.yml` without following links and compares exact bytes.

Missing or different content triggers an error containing pack and hook identity. LunaPack restores the exact original bytes through atomic replacement, aborts remaining work, and invokes existing transaction rollback for managed files, lock state, and configuration. This detects persisted changes at every process boundary. It cannot prove that a same-user process never changed and restored identical bytes during its lifetime; because lifecycle code does not reread the file, that residual race cannot influence the active operation.

### Extend version 1 and migrate repository-owned YAML together

The project schema keeps `schemaVersion: 1` while adding required source names and project trust. `luna init` emits empty trust collections. Local, Git, and GitHub source-add commands accept the name before location data, reject names already used by any source type, and list names with existing source details. A user-settings schema defines global and local-user trust under `~/.lunapack/config.yml`.

All repository-owned `lunapack.yml` files, lock files, examples, fixtures, snapshots, and tests gain deterministic source names, source identities, and trust collections in the same change. Pack manifests need no edit unless they demonstrate hooks or transient suppression. The lock schema version remains unchanged at the user's direction; source identity extends the current provenance contract rather than introducing a parallel document generation.

### Record the durable security decision

Implementation adds an accepted ADR covering the executable-content boundary, explicit file/command union, no-follow snapshots, trust scopes and acknowledgement, source pinning, direct argv execution, transient suppression, graph phase ordering, manifest integrity, and rollback limit. Public developer documentation separately covers author syntax, consumer consent, script modes, trust commands, source switching, and the absence of an OS sandbox. `CHANGELOG.md` records the new CLI and manifest behavior.

## Risks / Trade-offs

The controls address silent repository self-authorization, trust-by-name rebinding, cross-source update drift, implicit shell parsing, common symlink leakage, mutable-source consent races, later-hook staging tampering, and persistent project-manifest modification. Arbitrary code execution cannot be made risk-free without a real operating-system sandbox and publisher authenticity model.

- **High residual: authorized code has the user's authority.** A hook can read ordinary user files, use filesystem credentials, access the network, start detached processes, or alter external services. -> Default prompt mode, explicit danger text, scoped trust, direct argv, process-tree cancellation, and documentation reduce accidental authorization but do not contain authorized code.
- **High residual: trusted sources and pack pairs cover future content.** Repository compromise or a malicious future version remains trusted within the selected scope. -> Source-plus-pack trust is narrower than source trust; source identity prevents rebinding; source pinning, inspection, dry-run, and local scope are preferred. Cryptographic publisher signatures remain future work.
- **High residual: global and source trust have broad blast radius.** One confirmation can authorize many future hooks. -> Danger panel shows scope and identities; global trust requires explicit `--global`; project declarations require local acknowledgement; revocation/listing must be documented and implemented alongside creation.
- **Medium residual: child processes inherit ambient environment and runtime context.** Environment variables or tool-specific credential stores may expose secrets. -> Documentation warns users, output redacts common secret patterns, and implementation should pass only the platform environment needed for compatibility where test evidence permits. Complete secret isolation requires sandboxing.
- **Medium residual: post hooks can create irreversible effects before failing.** -> Restore all LunaPack-owned files and state, stop remaining hooks, identify the failed hook, and report that external effects may remain.
- **Medium residual: user settings can be modified by another process running as the same user.** -> Owner-only permissions, no-link path validation, strict schema validation, and atomic writes protect against other principals and corruption, not a compromised user session.
- **Medium residual: `--scripts run` intentionally bypasses per-hook consent and `--scripts skip` may produce a semantically incomplete installation.** -> Both are explicit invocation modes; dry-run and documentation show effective behavior; no mode is persisted.
- **Low residual: a hook can change and restore identical `lunapack.yml` bytes before exit.** -> Active lifecycle state is immutable in memory and never reloaded; boundary checks catch persistent changes. Strong prevention requires process isolation unavailable in scope.
- **Low residual: same-user staging races may exist between final digest verification and a runner reopening a packed file.** -> Private read-only staging, no-follow reads, per-hook digest verification, and immediate launch minimize the window. Eliminating it requires handle-based runner integration or sandboxing.
- **Operational trade-off: snapshotting and source pinning increase disk use and may leave newer versions unseen.** -> Stage only one operation, clean deterministically, and report when other sources contain versions that require explicit source-switch intent.
- **Compatibility trade-off: required source names and source identities invalidate repository-owned unnamed version-1 examples.** -> Update checked-in YAML and validation fixtures atomically without changing schema versions.

## Migration Plan

1. Add the ADR and schema/model support for hook declarations, transient suppression, source identity, scoped trust, and user settings while retaining existing schema versions.
2. Update all checked-in project and lock YAML, examples, fixtures, and source-add tests with deterministic names, identities, and empty trust collections where appropriate.
3. Carry normalized identities through source discovery, graph provenance, and lock persistence; pin updates and add explicit source-switch confirmation.
4. Add cross-platform user settings, danger-gated local/project/global trust commands, project acknowledgement, listing, and revocation.
5. Add no-follow snapshot materialization, transient hook planning, suppression, script modes, authorization, direct process execution, manifest integrity checks, and lifecycle transaction integration.
6. Add inspect and dry-run rendering, public/internal documentation, residual-risk guidance, and changelog entry.
7. Validate schemas, unit and integration tests, formatting, normal CLI publish, and Native AOT publish.

Rollback requires reverting schema, model, CLI, fixtures, and repository-owned YAML together. No persisted lock migration is required.
