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
- Trusted-publisher bindings, protected branches, package permissions, and
  deployment environments remain external service controls.
- Website artifacts cross from a read-only build job into a privileged deployment
  job.

## Spoofing

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

## Repudiation

### TM-R02: Release Action Denial

| Field | Assessment |
| --- | --- |
| Component | Release workflows and publication services |
| Asset | Evidence of what source, version, and artifact was published |
| Scenario | A publisher or operator disputes which artifact was released after an incident. |
| Preconditions | Workflow or external audit data is unavailable, or mutable source metadata was used. |
| Likelihood | Low |
| Impact | Incident response cannot reconstruct publication confidently. |
| Severity | Medium |
| Controls | Exact Git commits, GitHub workflow logs, release provenance, checksums, and immutable asset comparison. |
| Fix | Retain release evidence and prefer immutable refs. |
| Priority | Operational requirement |
| Status | Mitigated |
| Verification | Release-rerun, checksum, and workflow contract tests. |
| Residual risk | LunaPack does not maintain an independent append-only publication log. External audit retention remains service-controlled. |

## Elevation of Privilege

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
| Controls | Read-only build job, generated-site artifact handoff, dependent deployment job with job-scoped Pages and OIDC permissions, protected branch expectations, full-SHA action pins, and GitHub Pages environment controls. |
| Fix | Implemented by separating website generation from the job that holds deployment authority. |
| Priority | Implemented release control |
| Status | Fixed |
| Verification | `Scenario_WebsiteRelease_BuildRunsWithoutDeploymentCredentials` passes. |
| Residual risk | Compromised build dependencies can alter generated site content, but cannot directly use Pages write or OIDC credentials. Branch and environment protection remain external controls. |

## Maintainer Actions

1. Bind release identities to the exact repository, workflow, and environment.
2. Keep third-party actions pinned to reviewed full commit SHAs.
3. Preserve source, tag, workflow-run, and artifact relationships during release.
4. Retain redacted workflow, provenance, checksum, and publisher evidence.
5. Keep dependency builds separate from publication credentials.
6. Publish and validate immutable OCI digests when digest-centered distribution is
   implemented.

Use the [release security review](security-review.md) for the current release
verdict, detailed findings, validation evidence, and remediation priorities.
