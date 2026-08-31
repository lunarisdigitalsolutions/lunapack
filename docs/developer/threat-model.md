# LunaPack Threat Model

This explanation describes LunaPack's public trust boundaries, repository-specific
threats, implemented controls, and residual risks. LunaPack manages files from
local or Git sources and can run pack-provided lifecycle scripts. It is not a
sandbox or privilege boundary. Treat every pack and source as publisher code.

## Trust Boundaries

- A project owner chooses local directories or Git repositories as pack sources.
- Luna reads project configuration, lock state, manifests, templates, selected
  files, and the current workspace.
- Managed-file transactions cross from resolved pack content into the workspace.
- Approved lifecycle scripts cross into a process with the current user's authority.
- Git hosts, npm, NuGet, GitHub Releases, GHCR, and GitHub Actions are external
  identity, transport, build, and publication boundaries.
- The operating system, Git client, container runtime, and distribution services
  remain responsible for their own security boundaries.

## Spoofing

### TM-S01: Source or Publisher Impersonation

| Field | Assessment |
| --- | --- |
| Component | Configured local and Git sources, pack resolution |
| Asset | Pack identity, selected source, resolved content |
| Scenario | An attacker presents a repository, moved tag, or duplicate pack ID as content from the expected publisher. |
| Preconditions | The consumer adds the source, accepts a mutable ref, or resolves an ambiguous ID. |
| Likelihood | Medium |
| Impact | Malicious files or hooks can be selected under a trusted-looking pack ID. |
| Severity | High |
| Controls | Normalized source identities, source-name binding, exact Git commit lock evidence, ID conflict rejection, graph-wide external-source consent, and dry-run reporting. |
| Fix | Use full commit IDs for immutable automation. Publisher signatures and rollback-resistant metadata are required before introducing a hosted catalog. |
| Priority | Future hardening before hosted-catalog launch |
| Status | Mitigated for current local and configured Git sources |
| Verification | Source identity, mutable-ref, conflict, consent, and lock-provenance tests. |
| Residual risk | A pack ID and locked commit establish provenance, not cryptographic publisher identity. Protected tags remain a Git-host policy. |

### TM-S02: Distribution Substitution

| Field | Assessment |
| --- | --- |
| Component | GitHub Releases, npm, NuGet, GHCR, installer packages |
| Asset | Luna binaries and package identity |
| Scenario | A compromised registry account, mutable image tag, or unrelated workflow artifact supplies different bytes for an expected version. |
| Preconditions | Publisher or workflow authority is compromised, or automation trusts a mutable tag without independent verification. |
| Likelihood | Low |
| Impact | Consumers execute a substituted CLI with their own authority. |
| Severity | High |
| Controls | Workflow-bound npm and NuGet trusted publishing, OIDC publication, npm provenance, temporary NuGet credentials, full-SHA action pins, tag-commit artifact binding, immutable release-asset comparison, and published SHA-256 checksums. |
| Fix | Publish and validate OCI digests. |
| Priority | OCI digest verification after release |
| Status | Partially mitigated |
| Verification | Exact npm and NuGet trusted-publisher bindings, release workflow contract tests, release dry run, and package inspection. |
| Residual risk | Checksums hosted beside an artifact do not independently prove publisher identity. Mutable container tags can move. |

## Tampering

### TM-T01: Workspace Escape Through Filesystem Aliases

| Field | Assessment |
| --- | --- |
| Component | Install, update, Luna Link, move, uninstall, rollback, and project-state persistence |
| Asset | Files inside and outside the selected workspace |
| Scenario | A workspace path component is a symbolic link, junction, or reparse point, or a destination is hard-linked to a file outside the project. |
| Preconditions | The alias exists before mutation or another same-user process can alter the workspace. |
| Likelihood | Medium |
| Impact | Arbitrary file overwrite or deletion with the invoking user's permissions. |
| Severity | High |
| Controls | Rooted and traversal rejection, full-plan alias preflight, direct-path alias checks, and sibling-file replacement for hard-linked destinations. |
| Fix | Implemented for deterministic aliases. Handle-relative no-follow traversal remains future hardening. |
| Priority | Implemented release control; race hardening deferred |
| Status | Fixed with residual same-user race |
| Verification | Deterministic reparse-point tests, real symbolic-link security tests, and hard-link replacement tests. |
| Residual risk | Path inspection and mutation are separate operations; a same-user process can race component replacement. |

### TM-T02: Lock or Git Cache Poisoning

| Field | Assessment |
| --- | --- |
| Component | `lunapack-lock.yml`, project Git cache, source materialization |
| Asset | Resolved source identity, commit, manifest, target ownership, and selected bytes |
| Scenario | An attacker edits local lock or cache data so Luna reuses content from another source, commit, path, or manifest. |
| Preconditions | The attacker can modify the project or cache as the same user. |
| Likelihood | Medium |
| Impact | Unreviewed bytes can enter planning, overwrite owned files, or influence lifecycle selection. |
| Severity | High |
| Controls | Typed lock validation, configured-source ownership matching, immutable Git commit resolution, cache identity and commit validation, manifest checks, and blob verification before reuse. |
| Fix | Cache entries now fail validation or are repaired before project mutation. Keep lock changes under source review. |
| Priority | Implemented |
| Status | Fixed for cache substitution; local lock integrity remains user-controlled |
| Verification | Invalid, escaping, identity-mismatched, commit-mismatched, and content-mismatched cache tests. |
| Residual risk | A same-user attacker who can modify the workspace can also modify project configuration and lock evidence. Luna does not sign local state. |

### TM-T03: Ambiguous or Malformed Documents

| Field | Assessment |
| --- | --- |
| Component | Project configuration, lock files, pack manifests, custom YAML converters, JSON Schemas |
| Asset | Parsed source, parameter, target, variable, and ownership semantics |
| Scenario | Duplicate keys, unknown properties, malformed numeric values, or escaping paths are interpreted inconsistently or bypass intended validation. |
| Preconditions | A user opens or consumes an attacker-controlled repository or pack. |
| Likelihood | Medium |
| Impact | Configuration confusion, denial of service, or unsafe target selection. |
| Severity | Medium |
| Controls | Typed parsing, duplicate and unknown-property rejection, handled YAML errors, runtime validation, and schema parity for managed and lock targets. |
| Fix | Implemented for source, pack-parameter, and scalar-dictionary converters and target confinement. |
| Priority | Implemented |
| Status | Fixed for reviewed version-1 documents |
| Verification | Malformed configuration, duplicate parameter, target traversal, and runtime/schema parity tests. |
| Residual risk | Complete parser byte, nesting, and collection ceilings are not yet defined; see TM-D01. |

## Repudiation

### TM-R01: Lifecycle or Release Action Denial

| Field | Assessment |
| --- | --- |
| Component | Lifecycle trust decisions, lock evidence, CLI diagnostics, release workflows |
| Asset | Evidence of what source, script, version, and artifact was authorized |
| Scenario | A publisher or operator disputes which code ran or which artifact was released after an incident. |
| Preconditions | Local output was not retained, external audit data is unavailable, or mutable source metadata was used. |
| Likelihood | Low |
| Impact | Incident response cannot reconstruct authorization or publication confidently. |
| Severity | Medium |
| Controls | Source fingerprints, exact Git commits, rendered hook arguments before consent, lock records, GitHub workflow logs, release provenance, checksums, and immutable asset comparison. |
| Fix | Retain release evidence and relevant redacted diagnostics. Prefer immutable refs. |
| Priority | Operational requirement |
| Status | Mitigated |
| Verification | Trust, lock-provenance, release-rerun, checksum, and workflow contract tests. |
| Residual risk | LunaPack collects no telemetry and does not maintain an independent append-only audit log. |

## Information Disclosure

### TM-I01: Secret Disclosure to Approved Hooks

| Field | Assessment |
| --- | --- |
| Component | Lifecycle process execution |
| Asset | Credentials and private values in the parent environment or accessible files |
| Scenario | An approved hook reads ambient cloud tokens, CI variables, credentials, or unrelated user files and sends or prints them. |
| Preconditions | The user authorizes the hook or uses `--scripts run`. |
| Likelihood | Medium |
| Impact | Credential theft and access to resources available to the current user. |
| Severity | High |
| Controls | Explicit script modes, scoped trust, dominant persistent denial, pre-authorization of every hook, literal argument lists, and clear non-sandbox documentation. |
| Fix | Use `--scripts skip` for untrusted automation and minimize credentials. A future environment allowlist should remove ambient secrets. |
| Priority | Should address soon after release |
| Status | Open, explicitly accepted execution boundary |
| Verification | Trust-policy and process argument tests; environment-minimization tests are pending. |
| Residual risk | Even with a smaller environment, approved code retains the invoking user's filesystem, process, and network authority. |

### TM-I02: Source Disclosure Through Snapshot Entries

| Field | Assessment |
| --- | --- |
| Component | Operation pack snapshotting |
| Asset | Files outside the selected pack root |
| Scenario | A pack contains a link or special object that redirects snapshot copying to unrelated local data. |
| Preconditions | The source tree contains an unsupported entry or is changed concurrently by another same-user process. |
| Likelihood | Low |
| Impact | Unrelated bytes could be staged, hashed, rendered, copied, or exposed to an approved hook. |
| Severity | Medium |
| Controls | Linked roots fail; child links, reparse points, devices, and unsupported objects emit warnings and are skipped while regular siblings continue. |
| Fix | Deterministic alias following is fixed. Handle-relative no-follow copying remains future hardening. |
| Priority | Implemented release control; race hardening deferred |
| Status | Fixed with residual same-user race |
| Verification | Deterministic warning-and-skip test plus real file-link, directory-link, install, and update tests on symlink-capable hosts. |
| Residual risk | A concurrent same-user attacker may replace an inspected entry before it is opened. Skipping an entry may leave a pack incomplete. |

## Denial of Service

### TM-D01: Resource Exhaustion From Crafted Content

| Field | Assessment |
| --- | --- |
| Component | YAML parsing, source discovery, graph resolution, selection, snapshotting, and Scriban rendering |
| Asset | CLI availability, memory, CPU, disk, and workspace integrity |
| Scenario | A very large or deeply nested pack, graph, template, or selected file set exhausts resources. |
| Preconditions | The consumer evaluates or installs attacker-controlled content. |
| Likelihood | Medium |
| Impact | Process termination, disk exhaustion, long execution, or partial external hook effects. |
| Severity | Medium |
| Controls | Graph cycle and conflict checks, template computation limits, cancellation paths, private temporary workspaces, and transactional project mutation. |
| Fix | Add one fixed resource-limit policy covering document bytes and depth, graph nodes, files, byte totals, parameter sizes, and rendered output. |
| Priority | Should address soon after release |
| Status | Open |
| Verification | Existing cycle, conflict, malformed-input, and template tests; exact-boundary resource tests are pending. |
| Residual risk | Current public schemas do not define complete resource ceilings. Use disposable workspaces for unfamiliar packs. |

## Elevation of Privilege

### TM-E01: User-Authority Execution by Lifecycle Scripts

| Field | Assessment |
| --- | --- |
| Component | Lifecycle hook authorization and process launch |
| Asset | Current user's files, credentials, processes, and network access |
| Scenario | A malicious pack convinces a user or automation to approve a hook that performs arbitrary actions. |
| Preconditions | Script execution is explicitly approved, previously trusted, or selected with `--scripts run`. |
| Likelihood | Medium |
| Impact | Arbitrary code execution with the invoking user's full ambient authority. |
| Severity | High |
| Controls | Prompt, run, and skip modes; source-scoped trust; dominant denial; escaped preview; direct executable resolution; literal argv; snapshot hashing; and pre-mutation authorization. |
| Fix | Treat approval as code execution. Use denial or skip mode where hooks are not required. LunaPack does not claim sandboxing. |
| Priority | Permanent trust boundary |
| Status | Accepted by design |
| Verification | Trust-scope, denial, argument, digest, cancellation, and rollback tests. |
| Residual risk | Authorized code can create irreversible external effects that no transaction can restore. |

### TM-E02: Website Build Uses Deployment Authority

| Field | Assessment |
| --- | --- |
| Component | GitHub Actions website release workflow and npm build dependencies |
| Asset | GitHub Pages publication and OIDC authority |
| Scenario | A compromised build dependency executes during site generation in the same job that can deploy Pages content. |
| Preconditions | Malicious dependency code reaches a push build on the protected default branch. |
| Likelihood | Low |
| Impact | Unauthorized website deployment, content substitution, or misuse of job identity. |
| Severity | High |
| Controls | Protected branch expectations, full-SHA action pins, static output artifact, and GitHub Pages environment controls. |
| Fix | Build in a read-only job, upload the artifact, and grant Pages write plus OIDC only to a dependent deployment job. |
| Priority | Must address before public release |
| Status | Open |
| Verification | Workflow contract requires build/deploy separation and currently fails until the workflow is corrected. |
| Residual risk | Environment protection reduces unauthorized deployment but does not justify giving dependency execution publication credentials. |

## Safer Operation

1. Add only sources whose identity and ownership you reviewed.
2. Pin full Git commits and exact Luna versions in reproducible automation.
3. Inspect unfamiliar packs and run `--dry-run` before applying changes.
4. Review managed targets, external sources, templates, instructions, and hook
   arguments.
5. Use `--scripts skip` or the narrowest trust scope that satisfies the workflow.
6. Keep `lunapack-lock.yml` under review and use `luna audit` for drift.
7. Run untrusted evaluation in a disposable workspace with minimal credentials.
8. Verify release archives with `SHA256SUMS.txt`; prefer immutable image digests
   when available.

Diagnostics and hook output may contain private paths, URLs, usernames, or tool
output. Redact them before sharing. LunaPack does not collect telemetry.

Report suspected vulnerabilities through the
[Security Policy](https://github.com/lunarisdigitalsolutions/lunapack/blob/main/SECURITY.md).
