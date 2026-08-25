## 1. Architecture and Manifest Contracts

- [x] 1.1 Create ADR-0040 from the repository template for lifecycle executable content, no-follow snapshots, direct argv execution, trust scopes and acknowledgement, source pinning, transient suppression, manifest integrity, graph ordering, and rollback limits; add it to the ADR index.
- [x] 1.2 Extend `pack.yml` schema and manifest models with the four hook members and exclusive packed-file/runner or direct-command forms, including argument, description, and safe-path validation.
- [x] 1.3 Extend composite reference schema and models with unique `disabledHooks` lifecycle values and preservation through parsing, graph resolution, and inspection.
- [x] 1.4 Extend version-1 project configuration schema and models with required unique source names and project source/source-plus-pack trust declarations; retain schema version `1`.
- [x] 1.5 Define cross-platform `~/.lunapack/config.yml` models and validation for global-user trust, canonical-path local-project trust, and exact project-trust acknowledgements.
- [x] 1.6 Extend existing lock schema/models so every root and transient pack persists source name, normalized configured-source identity, and Git commit provenance where applicable without changing the lock schema version.
- [x] 1.7 Add schema/model tests for valid hook forms, suppression, omitted scripts, mixed or incomplete forms, unsafe paths, duplicate names/trust, source-plus-pack binding, canonical project keys, and source identity provenance.
- [x] 1.8 Update repository-owned project and lock YAML, examples, test fixtures, and snapshots with deterministic source names, identities, and trust collections while retaining existing schema versions.

## 2. Source Identity and Scoped Trust

- [x] 2.1 Normalize local physical paths and Git URL/ref/repository paths into configured-source identities; carry names and identities through dispatch, discovery, catalog candidates, resolved graphs, and lock persistence.
- [x] 2.2 Change local, Git, and GitHub source-add syntax to require `<name>` before location input; validate names ordinally across all source types and preserve existing location validation.
- [x] 2.3 Include source name, type, normalized location details, and trust-relevant identity in `luna sources list` formatting without changing source precedence.
- [x] 2.4 Implement a cross-platform user-settings store beneath the OS user profile with owner-only permissions, no-link/reparse validation, strict schema validation, canonical physical project keys, and same-directory atomic writes.
- [x] 2.5 Add danger-gated `luna trust source <name>...` and `luna trust pack <id>... --source <name>` commands with local-user default, mutually exclusive `--project`/`--global`, complete prevalidation, atomic saves, and no non-interactive confirmation bypass.
- [x] 2.6 Persist `--project` declarations in `lunapack.yml` plus exact user-local acknowledgements; ignore declarations when project path or source identities differ, and bind every pack trust entry to source identity plus bare pack ID.
- [x] 2.7 Add trust listing and revocation for local-user, project, and global-user scopes so users can audit and remove persistent execution authority.
- [x] 2.8 Add focused source, provenance, settings, and trust tests for multi-value atomic failure, source-name rebinding, project-file self-authorization attempts, scope separation, permissions, links in settings paths, path aliases, Windows-style paths, and cross-platform home resolution.

## 3. Immutable Hook and Source Planning

- [x] 3.1 Add operation-scoped snapshot materialization for local and Git candidates with user-private staging, deterministic cleanup, and all managed-file and hook reads bound to one snapshot.
- [x] 3.3 Canonicalize packed hook files beneath the snapshot root, hash each displayed invocation, and verify the digest immediately before execution.
- [x] 3.4 Add a lifecycle planner that classifies direct and transient nodes as install, incoming-release update, unchanged, or removed and emits stable dependency-first phases.
- [x] 3.5 Aggregate each transient node's disabled lifecycle types as the union of incoming references while preserving unsuppressed behavior for the same pack acting as a directly requested root.
- [x] 3.6 Pin ordinary update candidate selection to every lock node's configured-source identity; for explicit versions, prefer the locked source and model a source-switch confirmation when only another source can satisfy the request.
- [ ] 3.7 Add focused snapshot/planner tests for link leakage, source swaps during copy, staging tampering, cleanup, transient install/update hooks, most-restrictive suppression, direct-root behavior, incoming update hooks, new dependencies, unchanged nodes, and source pinning.

## 4. Authorization and Process Execution

- [x] 4.1 Add `--scripts <prompt|run|skip>` parsing with `prompt` default; apply suppression and skip before trust, keep run invocation-only, and reject unsupported values.
- [x] 4.2 Add one shared trust policy using effective local-user, acknowledged project, and global-user source identities or source-plus-pack-ID pairs; ensure root trust never flows to dependencies.
- [x] 4.3 Add structured confirmation formatting showing pack ID/version, exact source identity, hook, optional description, packed relative file, canonical executable, and separately escaped arguments without staging-path leakage.
- [x] 4.4 Fail closed when hook or source-switch confirmation is declined or unavailable, and authorize the complete operation plan before any hook or managed-file mutation.
- [x] 4.5 Resolve runners and commands to one canonical executable before display and execution; add a direct process executor with shell disabled, literal arguments, project-root working directory, sanitized terminal output, failure context, and child-tree termination on cancellation.
- [x] 4.6 Add focused policy, formatter, and executor tests for all script modes, trust scopes and precedence, source rebinding, untrusted dependencies, non-interactive denial, metacharacters, executable path stability, packed-file argv, process failure, cancellation, output controls, and no-shell behavior.

## 5. Install and Update Integration

- [x] 5.1 Integrate script modes, transient suppression, authorization, dependency-first pre-install execution, managed-file application, post-install execution, final digests, and source provenance into install.
- [x] 5.2 Integrate the same policy into every update form using incoming update hooks, install hooks for new dependencies, locked-source candidate selection, and confirmed explicit source switching.
- [x] 5.3 Back up exact `lunapack.yml` bytes before hooks, retain immutable in-memory configuration, verify existence and exact bytes after every process, and restore plus abort immediately on mutation.
- [x] 5.4 Extend lifecycle transactions so pre-hook failure prevents mutation and post-hook, manifest-integrity, or source-switch failure restores managed files, configuration, and lock state while warning that external effects may remain.
- [x] 5.5 Extend dry-run output with transient suppression, ordered hooks, trust/consent status, locked source, and proposed source switches while guaranteeing no prompt or process execution.
- [x] 5.6 Add unit and process-level integration tests for prompt/run/skip, transient scripts and suppression, all trust scopes, declined hooks, dry-run, pre/post failure, project-manifest modification/removal, rollback, source pinning/switching, multi-pack, composite, and non-interactive paths.

## 6. Inspection and Documentation

- [x] 6.1 Extend pack inspection with lifecycle hooks, descriptions, exact argv, composite-reference disabled hooks, and explicit no-script/no-suppression states; add formatter/command tests.
- [x] 6.2 Update manifest reference, source configuration, install/update guides, CLI reference, lifecycle safety explanation, and navigation for hook authoring, transient suppression, script modes, named sources, source pinning/switching, and scoped trust/list/revoke commands.
- [x] 6.3 Document internally the source-identity, user-settings, acknowledgement, no-follow snapshot, path-confinement, authorization, manifest-integrity, transaction, and process boundaries without describing execution as sandboxed.
- [x] 6.4 Publish the severity-ranked residual-risk assessment, especially user-authority execution, future trusted content, ambient credentials, irreversible effects, settings compromise, skip/run trade-offs, and same-user races.
- [x] 6.5 Add an externally observable changelog entry and schema-valid examples or test packs covering packed-file/direct-command hooks and transient suppression.

## 7. Verification

- [x] 7.1 Run CSharpier and repository formatters on changed C#, Markdown, JSON, and YAML; run Markdown and schema validation.
- [x] 7.2 Run focused tests during each slice, then the complete LunaPack CLI unit and integration test projects on supported target/runtime combinations.
- [ ] 7.3 Validate owner permissions and no-link behavior on Windows and Unix test environments, including user-profile settings resolution and canonical project aliases.
- [ ] 7.4 Validate the CLI package lock, build the solution, publish the normal CLI, and publish the Native AOT CLI for the current runtime.
- [x] 7.5 Run `openspec validate add-secure-pack-lifecycle-scripts --strict` and confirm all planning artifacts remain consistent with implemented behavior.

> Deferred: link/reparse snapshot validation in 3.7 and owner/no-link validation in
> 7.3 remain intentionally deferred under ADR-0040. Native AOT publication in 7.4
> is blocked on this machine by the missing Visual Studio Desktop C++ workload.
