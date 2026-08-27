# Threat Model

This document describes LunaPack's public security boundaries, controls, and
residual risks. It covers the CLI, pack sources, manifests, lock files,
lifecycle hooks, release packages, container image, website, and update
channels.

## Scope And Assumptions

Protected assets include consumer repository files, lock-file integrity, source
provenance, user trust settings, credentials available to Luna or an executable
lifecycle script, release signing identity, and published artifacts.

Actors include pack consumers, pack authors, source maintainers, contributors,
release maintainers, package registries, and attackers able to modify a source,
repository, dependency, workflow input, or local filesystem object.

Trust boundaries exist between:

- Luna and project-controlled YAML, templates, lock files, and filesystem state.
- A consumer repository and each local or Git pack source.
- Luna and an approved lifecycle process running with user authority.
- GitHub Actions and GitHub Releases, npm, NuGet, and GitHub Container Registry.
- A container and its mounted host repository.

LunaPack assumes the operating system, Git executable, .NET runtime or native
host, container runtime, and selected registries enforce their documented
security boundaries. LunaPack is not a sandbox or privilege boundary.

## Data Flows And Privileged Operations

1. Luna loads project configuration and the generated lock file.
2. It discovers local or Git pack manifests and selected files.
3. It validates schemas, resolves an exact dependency graph, binds parameters,
   evaluates conditions, and renders templates.
4. It plans writes, merges, moves, or removals within the selected workspace.
5. It may display lifecycle instructions or launch approved lifecycle commands
   with the invoking user's authority.
6. It persists configuration and lock state after successful filesystem work.
7. Release automation builds native binaries, stages npm and NuGet packages,
   builds an OCI image from the Linux binary, and publishes through federated
   identities.

## Risk Method

Impact and likelihood each use a four-point scale.

| Score | Impact                                           | Likelihood                       |
| ----- | ------------------------------------------------ | -------------------------------- |
| 1     | Minor local inconvenience                        | Unlikely under expected use      |
| 2     | Recoverable repository or availability loss      | Plausible with unusual access    |
| 3     | Material repository, credential, or release harm | Plausible for untrusted input    |
| 4     | Broad compromise or irreversible sensitive loss  | Expected when preconditions hold |

Severity equals impact multiplied by likelihood: 1-3 Low, 4-7 Medium, 8-11
High, and 12-16 Critical. Priority reflects release timing, not severity alone:
Must address before public release, Should address soon after release, Planned
hardening, or Accepted or informational.

## Threat Records

### LP-T01: Malicious Lifecycle Execution

- **STRIDE:** Tampering, information disclosure, elevation of privilege.
- **Component:** Pack lifecycle script hooks and trust settings.
- **Scenario:** An approved pack command reads credentials, changes unrelated
  files, accesses the network, or starts another process.
- **Preconditions:** A consumer selects `run`, grants matching trust, or confirms
  execution while holding valuable ambient authority.
- **Impact:** 4.
- **Likelihood:** 3.
- **Severity:** Critical (12).
- **Existing controls:** Default prompt mode, explicit skip and run modes,
  source-identity trust, dependency-specific authorization, immutable operation
  snapshots, script hashing before launch, direct argument lists, and project
  manifest restoration. Instruction hooks never launch processes and remain
  outside script trust.
- **Recommended mitigation:** Review pack source and script content; use least
  privilege; add stronger isolation only when it can preserve required tooling.
- **Priority:** Accepted or informational.
- **Status:** Accepted product boundary; prominently documented.
- **Verification:** Lifecycle authorization, snapshot, cancellation, and rollback
  tests.
- **Residual risk:** Approved code retains the invoking user's effective access.

### LP-T02: Source Impersonation And Dependency Confusion

- **STRIDE:** Spoofing and tampering.
- **Component:** Local/Git sources, pack IDs, and composite dependencies.
- **Scenario:** A similarly named source or pack supplies content other than the
  consumer intended.
- **Preconditions:** A consumer configures or trusts the wrong source, or an
  authorized source changes ownership.
- **Impact:** 4.
- **Likelihood:** 2.
- **Severity:** High (8).
- **Existing controls:** Named configured sources, normalized source identity,
  exact composite versions, resolved Git commits, and lock provenance.
- **Recommended mitigation:** Add signed provenance and publisher identity before
  introducing a hosted catalog.
- **Priority:** Planned hardening.
- **Status:** Deferred until a hosted catalog is designed; current consumers
  explicitly select local or Git sources.
- **Verification:** Source identity, exact-version graph, and trust tests.
- **Residual risk:** Names and Git hosting accounts are not cryptographic
  publisher identities.

### LP-T03: Package Or Update Substitution

- **STRIDE:** Spoofing and tampering.
- **Component:** GitHub Releases, npm, NuGet, GHCR, and update channels.
- **Scenario:** A registry, publisher account, or release path serves a modified
  Luna artifact.
- **Preconditions:** Registry or release identity compromise, or consumer use of
  an unpinned mutable version.
- **Impact:** 4.
- **Likelihood:** 2.
- **Severity:** High (8).
- **Existing controls:** Exact release versions, SHA-256 archive list, pinned
  workflow actions, npm provenance, NuGet trusted publishing, OCI provenance and
  SBOM generation, and one source revision per release.
- **Recommended mitigation:** Verify registry trusted-publisher settings, retain
  immutable artifacts, and document digest verification for containers.
- **Priority:** Must address before public release.
- **Status:** Code configured; external registry configuration unverified.
- **Verification:** Release dry run, package inspection, provenance checks, and
  post-publication identity checks.
- **Residual risk:** Compromise of a trusted registry or maintainer identity can
  still affect consumers.

### LP-T04: Release Workflow Compromise

- **STRIDE:** Tampering and elevation of privilege.
- **Component:** GitHub Actions and composite actions.
- **Scenario:** A dependency change, injected workflow value, or over-broad token
  alters release output or publishes unauthorized artifacts.
- **Preconditions:** Workflow modification, compromised action commit, or unsafe
  interpolation into a shell.
- **Impact:** 4.
- **Likelihood:** 2.
- **Severity:** High (8).
- **Existing controls:** Third-party actions pinned by commit, explicit job
  permissions, values passed through environment variables, tag validation,
  release artifact allowlists, and dry-run gates.
- **Recommended mitigation:** Require protected release environments and code
  ownership; run workflow security scanning on every change.
- **Priority:** Must address before public release.
- **Status:** Partially mitigated; repository settings require maintainer review.
- **Verification:** Workflow contract tests and repository-setting review.
- **Residual risk:** A malicious reviewed workflow change can use granted release
  permissions.

### LP-T05: Malformed Or Deceptive State

- **STRIDE:** Tampering and denial of service.
- **Component:** YAML parsing, JSON Schemas, configuration, lock files, and pack
  manifests.
- **Scenario:** Crafted state bypasses validation, triggers unsafe defaults, or
  causes inconsistent deserialization.
- **Preconditions:** An attacker can modify repository or source documents.
- **Impact:** 3.
- **Likelihood:** 2.
- **Severity:** Medium (6).
- **Existing controls:** Closed schemas, typed models, model validation, one
  public schema version, exact enum values, and required provenance fields.
- **Recommended mitigation:** Add explicit document-size and nesting limits in a
  future resource-hardening change and retain negative schema fixtures.
- **Priority:** Planned hardening.
- **Status:** Deferred for the first release; current sources are selected by
  the consumer.
- **Verification:** Schema fixtures, model tests, and malformed-state integration
  tests.
- **Residual risk:** Parser resource use is not independently bounded before
  deserialization.

### LP-T06: Path Escape Or Arbitrary File Mutation

- **STRIDE:** Tampering and elevation of privilege.
- **Component:** Workspace resolution, managed targets, destinations, remapping,
  and removal.
- **Scenario:** A crafted path writes, moves, overwrites, or deletes content
  outside the selected repository.
- **Preconditions:** Attacker-controlled configuration, manifest, lock data, or
  CLI path input.
- **Impact:** 4.
- **Likelihood:** 2.
- **Severity:** High (8).
- **Existing controls:** Central project-relative path normalization, rejection
  of rooted and escaping paths, canonical workspace resolution, ownership
  records, digest checks, and transaction rollback.
- **Recommended mitigation:** Continue boundary tests for every new path input
  and reject unsupported filesystem object types.
- **Priority:** Must address before public release.
- **Status:** Mitigated; final security regression suite pending.
- **Verification:** Windows/Unix separator, traversal, destination, move, and
  rollback tests.
- **Residual risk:** Same-user filesystem races remain possible after validation.

### LP-T07: Link And Filesystem Race Attacks

- **STRIDE:** Tampering and elevation of privilege.
- **Component:** Source snapshots and managed-file transactions.
- **Scenario:** Symlinks, junctions, hard links, mount points, or a concurrent
  process redirect reads or writes after path validation.
- **Preconditions:** A same-user attacker can mutate source or workspace
  filesystem objects during an operation.
- **Impact:** 4.
- **Likelihood:** 2.
- **Severity:** High (8).
- **Existing controls:** Canonical project roots, immutable operation snapshots,
  digest checks, transactional restoration, and explicit no-follow limitation.
- **Recommended mitigation:** First reject links and non-regular snapshot
  entries, then add handle-based no-follow traversal and identity revalidation
  in future filesystem hardening.
- **Priority:** Planned hardening.
- **Status:** Both object-type rejection and stronger race confinement are
  deferred for the first release under ADR-0040.
- **Verification:** Future adversarial link and same-user race integration tests.
- **Residual risk:** Snapshot copying can follow attacker-controlled links.

### LP-T08: Unsafe Archive Extraction

- **STRIDE:** Tampering and elevation of privilege.
- **Component:** npm package staging and release artifacts.
- **Scenario:** A modified archive writes outside staging or introduces an
  unexpected executable during extraction.
- **Preconditions:** A release artifact is replaced after build or the artifact
  boundary is compromised.
- **Impact:** 4.
- **Likelihood:** 2.
- **Severity:** High (8).
- **Existing controls:** Release artifact name allowlist, exact count, isolated
  staging directory, generated checksums, archive-member path and type
  preflight, and post-extraction file-type checks.
- **Recommended mitigation:** Continue testing archive handling when supported
  targets or packaging tools change.
- **Priority:** Must address before public release.
- **Status:** Mitigated.
- **Verification:** Unsafe-path, special-entry, duplicate-entry, and valid-archive
  contract tests; release dry run remains pending.
- **Residual risk:** External archive tools remain part of the trusted build
  environment.

### LP-T09: Template Or File Resource Exhaustion

- **STRIDE:** Denial of service.
- **Component:** Scriban templates and managed source files.
- **Scenario:** A pack consumes excessive CPU, memory, disk, or output through a
  complex template or large file set.
- **Preconditions:** A consumer processes an untrusted or compromised pack.
- **Impact:** 2.
- **Likelihood:** 3.
- **Severity:** Medium (6).
- **Existing controls:** Scriban loop and recursion defaults, strict variables,
  UTF-8 validation, and transactional writes.
- **Recommended mitigation:** Define pack file-count, input-size, and rendered
  output limits in a future resource-hardening change and report limit failures
  clearly.
- **Priority:** Planned hardening.
- **Status:** Deferred for the first release; trusted packs can still cause
  availability loss through excessive resource use.
- **Verification:** Boundary tests for loops, recursion, file counts, and sizes.
- **Residual risk:** Current limits do not bound every allocation or input file.

### LP-T10: Dependency Graph Exhaustion Or Cycles

- **STRIDE:** Denial of service.
- **Component:** Composite pack resolution.
- **Scenario:** Cyclic or excessively broad dependency graphs consume resources
  or prevent deterministic installation.
- **Preconditions:** A source contains attacker-controlled composite manifests.
- **Impact:** 2.
- **Likelihood:** 2.
- **Severity:** Medium (4).
- **Existing controls:** Cycle detection, exact dependency versions, complete
  graph planning before mutation, and duplicate identity checks.
- **Recommended mitigation:** Add explicit graph depth and node-count limits.
- **Priority:** Planned hardening.
- **Status:** Partially mitigated.
- **Verification:** Cycle, duplicate, conflict, and large-graph tests.
- **Residual risk:** Acyclic but very large graphs can consume excessive
  resources.

### LP-T11: Command, Argument, Or Environment Injection

- **STRIDE:** Tampering and elevation of privilege.
- **Component:** Git subprocesses and lifecycle hooks.
- **Scenario:** Untrusted text changes process structure or causes unintended
  command execution.
- **Preconditions:** Attacker controls source configuration, hook declarations,
  arguments, or inherited environment values.
- **Impact:** 4.
- **Likelihood:** 2.
- **Severity:** High (8).
- **Existing controls:** Shell execution disabled, executable and arguments
  separated through `ProcessStartInfo.ArgumentList`, schema validation, and
  explicit lifecycle authorization.
- **Recommended mitigation:** Maintain direct argv execution and define a
  reduced inherited-environment contract in a future lifecycle revision.
- **Priority:** Planned hardening.
- **Status:** Environment minimization is deferred; explicit trust remains the
  current execution boundary.
- **Verification:** Literal-argument tests and lifecycle process tests.
- **Residual risk:** An approved executable interprets its own arguments and
  inherited environment.

### LP-T12: Secret Or Personal Data Disclosure

- **STRIDE:** Information disclosure and repudiation.
- **Component:** Diagnostics, hook output, caches, settings, and release logs.
- **Scenario:** Errors or subprocess output expose credentials, local paths,
  personal data, or private source details.
- **Preconditions:** Sensitive data enters command arguments, environment,
  repository content, Git output, or hook output.
- **Impact:** 3.
- **Likelihood:** 2.
- **Severity:** Medium (6).
- **Existing controls:** No telemetry, bounded hook output, public reporting
  guidance, step-scoped release credentials, and ignored local caches.
- **Recommended mitigation:** Add redaction guidance and tests for known
  credential forms; avoid accepting secrets as CLI arguments.
- **Priority:** Must address before public release.
- **Status:** Current tree clean; final scan and history remediation pending.
- **Verification:** Secret scans of tree, history, packages, images, and logs.
- **Residual risk:** Arbitrary tools and hooks can print data Luna cannot
  identify reliably.

### LP-T13: Container Supply-Chain Or Runtime Escape

- **STRIDE:** Tampering and elevation of privilege.
- **Component:** OCI build, GHCR image, and mounted workspace.
- **Scenario:** A changed base image or privileged container modifies the host
  beyond intended repository access.
- **Preconditions:** Compromised base-image identity, mutable image tag, broad
  mount, or privileged runtime options.
- **Impact:** 4.
- **Likelihood:** 2.
- **Severity:** High (8).
- **Existing controls:** Digest-pinned runtime-only base, one-stage build from
  validated Native AOT output, non-root user, explicit work directory, OCI
  labels, SBOM, and provenance.
- **Recommended mitigation:** Scan the built image and mount only the intended
  repository before release; add immutable digest publication and verification
  in future OCI hardening.
- **Priority:** Must address before public release.
- **Status:** Image build and scanning remain release validation; immutable
  digest guidance is deferred.
- **Verification:** Image build, vulnerability scan, help/version smoke tests,
  and mounted-write ownership test.
- **Residual risk:** A mounted writable repository remains writable by the
  container identity.

### LP-T14: Incomplete Auditability And Recovery

- **STRIDE:** Repudiation and tampering.
- **Component:** Lock ownership, transaction rollback, and release reruns.
- **Scenario:** A partial write or interrupted multi-registry release leaves
  state whose origin or completeness is unclear.
- **Preconditions:** Filesystem, network, registry, or process failure during a
  mutation or release.
- **Impact:** 3.
- **Likelihood:** 2.
- **Severity:** Medium (6).
- **Existing controls:** Lock provenance and digests, preplanned transactions,
  rollback snapshots, checksums, duplicate-aware publishing, and dry-run
  preparation.
- **Recommended mitigation:** Verify every expected registry artifact after
  publication and fail reruns when an existing GitHub Release is incomplete.
- **Priority:** Should address soon after release.
- **Status:** Partially mitigated.
- **Verification:** Failure-injection tests, release contract tests, and
  post-publication artifact inventory.
- **Residual risk:** Luna cannot reverse external lifecycle side effects or
  registry publication.

## Security Guidance

Treat packs and their sources as code. Review source identity, exact versions,
managed targets, templates, and lifecycle commands before installation. Prefer
`--scripts skip` for automation that does not require hooks and use the least
privileged account that can modify the repository.

Report suspected vulnerabilities through the process in
[Security Policy](https://github.com/lunarisdigitalsolutions/lunapack/blob/main/SECURITY.md).
