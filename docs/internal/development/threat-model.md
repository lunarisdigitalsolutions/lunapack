# LunaPack Development Threat Model

This explanation describes threats to LunaPack's repository, release process,
distribution channels, and website publication. It helps maintainers assess
changes to build and deployment authority. The public
[usage threat model](../../developer/threat-model.md) covers risks encountered
when consuming packs and running Luna.

## Trust Boundaries

- Contributor changes and dependency code enter repository-controlled builds.
- Release workflows cross from reviewed source and tags into published artifacts.
- GitHub Actions, GitHub Releases, npm, NuGet, GHCR, and GitHub Pages are external
  identity, build, and publication boundaries.
- Installable OCI publishing workflow packs add registry identities, Azure
  workload federation, and self-hosted runner credential stores as external
  publishing boundaries.
- Trusted-publisher bindings, protected branches, package permissions, and
  deployment environments remain external service controls.
- Website artifacts cross from a read-only build job into a privileged deployment
  job.
- Pack configuration, local or remote pack data, graph declarations, template
  output, and OCI ZIP content cross into CLI parser, memory, CPU, temporary
  storage, and workspace-planning boundaries.
- Pack content and paths cross from repository-controlled bytes into human
  review, CLI diagnostics, project workspaces, and registry publication.
- Pack configuration, local or remote pack data, graph declarations, template
  output, and OCI ZIP content cross into CLI parser, memory, CPU, temporary
  storage, and workspace-planning boundaries.

## Spoofing

### TM-S02: Distribution Substitution

| Field         | Assessment                                                                                                                                                                                                                                                                                   |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Component     | GitHub Releases, npm, NuGet, GHCR, installer packages                                                                                                                                                                                                                                        |
| Asset         | Luna binaries and package identity                                                                                                                                                                                                                                                           |
| Scenario      | A compromised registry account, mutable image tag, or unrelated workflow artifact supplies different bytes for an expected version.                                                                                                                                                          |
| Preconditions | Publisher or workflow authority is compromised, or automation trusts a mutable tag without independent verification.                                                                                                                                                                         |
| Likelihood    | Low                                                                                                                                                                                                                                                                                          |
| Impact        | Consumers execute a substituted CLI with their own authority.                                                                                                                                                                                                                                |
| Severity      | High                                                                                                                                                                                                                                                                                         |
| Controls      | Workflow-bound npm and NuGet trusted publishing, OIDC publication, npm provenance, temporary NuGet credentials, full-SHA action pins, tag-commit artifact binding, immutable release-asset comparison, published SHA-256 checksums, and verified OCI manifest/content digests in lock state. |
| Fix           | Enforce immutable OCI version tags and retain digest verification for registry artifacts.                                                                                                                                                                                                    |
| Priority      | Registry-side tag immutability and publisher identity                                                                                                                                                                                                                                        |
| Status        | OCI content integrity is mitigated; publisher identity and tag immutability remain external controls                                                                                                                                                                                         |
| Verification  | Exact npm and NuGet trusted-publisher bindings, release workflow contract tests, release dry run, package inspection, OCI unit tests, and pack-validation integration tests.                                                                                                                 |
| Residual risk | Checksums and digests prove byte identity, not publisher identity. Registry policies must prevent mutable tags and independent-publisher races.                                                                                                                                              |

## Tampering

### TM-T05: Review Bypass Through Hidden Unicode

| Field         | Assessment                                                                                                                                                                                                                                 |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Component     | Pack implementation, review tooling, publication validation, managed paths, and terminal diagnostics                                                                                                                                       |
| Asset         | Maintainer review accuracy, exact release bytes, and trustworthy CLI output                                                                                                                                                                |
| Scenario      | A contributor or upstream pack uses bidi controls, zero-width characters, malformed encodings, BOM ambiguity, or terminal controls in paths so review or diagnostics differ from the processed bytes.                                      |
| Preconditions | A reviewer trusts rendered source, filenames, or terminal output without inspecting the reported Unicode code points or original bytes.                                                                                                    |
| Likelihood    | Medium                                                                                                                                                                                                                                     |
| Impact        | Deceptive text, identifiers, filenames, or output can bypass human review and reach consumers or release artifacts.                                                                                                                        |
| Severity      | High                                                                                                                                                                                                                                       |
| Controls      | Shared final-plan/raw-source/installed-target scanner, Bidi_Control and U+200B blocking, warning classes, valid emoji exemptions, UTF-8/BOM recovery, path rejection and escaping, 1,000-finding retention, and retained blocker evidence. |
| Fix           | Treat hidden-Unicode diagnostics as a release review gate. Preserve the explicit override for documented exceptions without allowing it to suppress output.                                                                                |
| Priority      | Maintainer review control                                                                                                                                                                                                                  |
| Status        | Implemented with documented residual semantic risk                                                                                                                                                                                         |
| Verification  | Unit, integration, security, local CLI smoke, path, truncation, opaque-encoding, authoring, lifecycle, publication, and audit tests.                                                                                                       |
| Residual risk | Unicode cannot establish benign intent. Editors, diffs, fonts, and terminals may disagree about valid text, and the override remains a human decision.                                                                                     |

## Repudiation

### TM-R02: Release Action Denial

| Field         | Assessment                                                                                                                  |
| ------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Component     | Release workflows and publication services                                                                                  |
| Asset         | Evidence of what source, version, and artifact was published                                                                |
| Scenario      | A publisher or operator disputes which artifact was released after an incident.                                             |
| Preconditions | Workflow or external audit data is unavailable, or mutable source metadata was used.                                        |
| Likelihood    | Low                                                                                                                         |
| Impact        | Incident response cannot reconstruct publication confidently.                                                               |
| Severity      | Medium                                                                                                                      |
| Controls      | Exact Git commits, GitHub workflow logs, release provenance, checksums, and immutable asset comparison.                     |
| Fix           | Retain release evidence and prefer immutable refs.                                                                          |
| Priority      | Operational requirement                                                                                                     |
| Status        | Mitigated                                                                                                                   |
| Verification  | Release-rerun, checksum, and workflow contract tests.                                                                       |
| Residual risk | LunaPack does not maintain an independent append-only publication log. External audit retention remains service-controlled. |

## Denial of Service

### TM-D01: Resource Exhaustion From Pack Inputs

| Field         | Assessment                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Component     | CLI configuration parsing, local/Git/OCI discovery, graph traversal, file selection, snapshotting, template rendering, and OCI archive handling                                                                                                                                                                                                                                                                                                      |
| Asset         | CLI availability, memory, CPU, disk, and project transaction state                                                                                                                                                                                                                                                                                                                                                                                   |
| Scenario      | A very large document, file tree, dependency graph, rendered output, or OCI artifact consumes resources before Luna can reject it.                                                                                                                                                                                                                                                                                                                   |
| Preconditions | A maintainer tests or automates a repository, pack source, Git ref, or registry artifact with attacker-controlled size or nesting.                                                                                                                                                                                                                                                                                                                   |
| Likelihood    | Medium                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Impact        | Process termination, disk exhaustion, long evaluation, or blocked automation.                                                                                                                                                                                                                                                                                                                                                                        |
| Severity      | High                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| Controls      | Non-overridable bootstrap reads; typed scoped limits; finite user ceilings; project reductions; byte-counted reads and copies; bounded Git records, measured cache accounting, aggregate catalogs, and OCI pagination; lock-collection YAML preflight; directory-inclusive snapshot counts; streaming link and file enumeration; shared graph, catalog, file, and rendered budgets; bounded OCI transfer/archive/extraction; pre-mutation rejection. |
| Fix           | Enforce the effective resource policy at every producer boundary, including publication, and keep path, identity, digest, and trust checks independent from size overrides.                                                                                                                                                                                                                                                                          |
| Priority      | Implemented release control                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Status        | Fixed for defined pack-input and lifecycle boundaries                                                                                                                                                                                                                                                                                                                                                                                                |
| Verification  | Unit, integration, and OCI security tests cover exact and over-limit inputs, lower-bound diagnostics, state preservation, raised-limit unsafe entries, and shared graph/output budgets.                                                                                                                                                                                                                                                              |
| Residual risk | User-owned raises can consume more resources by policy. CPU loops inside approved lifecycle scripts and parser nesting beyond byte ceilings remain separate risks.                                                                                                                                                                                                                                                                                   |

## Information Disclosure

### TM-I03: OCI Publishing Workflow Credentials

| Field         | Assessment                                                                                                                                                                                                                             |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Component     | Installable GitHub Actions OCI publishing workflow packs and `luna publish`                                                                                                                                                            |
| Asset         | Azure workload identity, native registry credentials, and private pack content                                                                                                                                                         |
| Scenario      | A changed workflow or overbroad job exposes a registry credential or publishes pack content to an unintended repository.                                                                                                               |
| Preconditions | A workflow with registry write permission runs on a changed pack or compromised protected-branch commit.                                                                                                                               |
| Likelihood    | Medium                                                                                                                                                                                                                                 |
| Impact        | Unauthorized reads or releases under the repository's registry identity.                                                                                                                                                               |
| Severity      | High                                                                                                                                                                                                                                   |
| Controls      | Full-SHA action pins, `main`/`packs/**` triggers, minimal per-job permissions, source-bound native credentials, Azure OIDC instead of a client secret, validated pack publication without hook execution, and immutable registry tags. |
| Fix           | Protect workflow and pack changes with review; scope registry roles and self-hosted runner credentials to the required repository.                                                                                                     |
| Priority      | Maintainer and registry policy                                                                                                                                                                                                         |
| Status        | Mitigated by maintained workflow packs; external identity and runner controls remain operator-owned                                                                                                                                    |
| Verification  | PowerShell fixtures cover duplicates, deleted/non-pack paths, whitespace, first pushes, malformed JSON/paths, and publish errors; pack integration, OCI unit, and security tests pass. Live identity tests need infrastructure.        |
| Residual risk | A compromised trusted branch can publish a structurally valid but harmful pack with its granted write identity.                                                                                                                        |

## Elevation of Privilege

### TM-E02: Website Build Uses Deployment Authority

| Field         | Assessment                                                                                                                                                                                                             |
| ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Component     | GitHub Actions website release workflow and npm build dependencies                                                                                                                                                     |
| Asset         | GitHub Pages publication and OIDC authority                                                                                                                                                                            |
| Scenario      | A compromised build dependency executes during site generation in the same job that can deploy Pages content.                                                                                                          |
| Preconditions | Malicious dependency code reaches a push build on the protected default branch.                                                                                                                                        |
| Likelihood    | Low                                                                                                                                                                                                                    |
| Impact        | Unauthorized website deployment, content substitution, or misuse of job identity.                                                                                                                                      |
| Severity      | High                                                                                                                                                                                                                   |
| Controls      | Read-only build job, generated-site artifact handoff, dependent deployment job with job-scoped Pages and OIDC permissions, protected branch expectations, full-SHA action pins, and GitHub Pages environment controls. |
| Fix           | Implemented by separating website generation from the job that holds deployment authority.                                                                                                                             |
| Priority      | Implemented release control                                                                                                                                                                                            |
| Status        | Fixed                                                                                                                                                                                                                  |
| Verification  | `Scenario_WebsiteRelease_BuildRunsWithoutDeploymentCredentials` passes.                                                                                                                                                |
| Residual risk | Compromised build dependencies can alter generated site content, but cannot directly use Pages write or OIDC credentials. Branch and environment protection remain external controls.                                  |

## Maintainer Actions

1. Bind release identities to the exact repository, workflow, and environment.
2. Keep third-party actions pinned to reviewed full commit SHAs.
3. Preserve source, tag, workflow-run, and artifact relationships during release.
4. Retain redacted workflow, provenance, checksum, and publisher evidence.
5. Keep dependency builds separate from publication credentials.
6. Configure immutable OCI version tags and preserve manifest/content digest
   verification for published and installed artifacts.
7. Investigate hidden-Unicode diagnostics and path rejections during pull
   request and release review; do not override them solely to clear CI.
8. Keep project resource limits at or below built-in defaults; review every
   user-owned increase and require a finite ceiling for new producer types.
9. Keep intermediate-output safeguards below any resource limit that can
   allocate retained collections; current Git/path records are capped at 1 MiB
   each and 64 MiB per enumeration, while aggregate catalog manifests are capped
   at 64 MiB.
