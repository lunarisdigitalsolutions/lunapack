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
- Private-pack examples add OCI registry identities, Azure workload federation,
  and scoped GitHub Actions tokens as external publishing boundaries.
- Trusted-publisher bindings, protected branches, package permissions, and
  deployment environments remain external service controls.
- Website artifacts cross from a read-only build job into a privileged deployment
  job.

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
| Verification  | Exact npm and NuGet trusted-publisher bindings, release workflow contract tests, release dry run, package inspection, and authenticated local TLS OCI publish/resolve tests.                                                                                                                 |
| Residual risk | Checksums and digests prove byte identity, not publisher identity. Registry policies must prevent mutable tags and independent-publisher races.                                                                                                                                              |

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

## Information Disclosure

### TM-I03: Private-Pack Workflow Credentials

| Field         | Assessment                                                                                                                                                                                                                      |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Component     | Private-pack GitHub Actions templates and `luna publish`                                                                                                                                                                        |
| Asset         | GHCR tokens, Azure workload identity, registry secrets, and private pack content                                                                                                                                                |
| Scenario      | A changed workflow or overbroad job exposes a registry credential or publishes pack content to an unintended repository.                                                                                                        |
| Preconditions | A workflow with registry write permission runs on a changed pack or compromised protected-branch commit.                                                                                                                        |
| Likelihood    | Medium                                                                                                                                                                                                                          |
| Impact        | Unauthorized reads or releases under the repository's registry identity.                                                                                                                                                        |
| Severity      | High                                                                                                                                                                                                                            |
| Controls      | Full-SHA action pins, `main`/`packs/**` triggers, minimal per-job permissions, source-bound credentials, Azure OIDC instead of a client secret, validated pack publication without hook execution, and immutable registry tags. |
| Fix           | Protect workflow and pack changes with review; scope registry roles to the required repository.                                                                                                                                 |
| Priority      | Maintainer and registry policy                                                                                                                                                                                                  |
| Status        | Mitigated by the maintained example; external permissions remain operator-owned                                                                                                                                                 |
| Verification  | Changed-pack fixtures, workflow contract checks, and authenticated local TLS registry integration; live provider identities require configured infrastructure.                                                                  |
| Residual risk | A compromised trusted branch can publish a structurally valid but harmful pack with its granted write identity.                                                                                                                 |

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
