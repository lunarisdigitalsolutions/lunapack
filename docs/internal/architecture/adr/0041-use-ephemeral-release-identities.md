---
status: accepted
date: 2026-08-24
decision-makers: LunaPack maintainers
---

# ADR-0041: Use Ephemeral Release Identities

## Context

LunaPack releases publish related artifacts to GitHub Releases, npm, NuGet, and
GitHub Container Registry. Long-lived registry tokens increase secret rotation,
exposure, and incident-response risk. GitHub Actions can instead prove workflow
identity through OpenID Connect, while GitHub-hosted publication can use its
scoped job token.

Release authentication must remain non-interactive, attributable to the release
workflow, and unavailable during dry runs.

## Decision

Registry publication uses ephemeral workflow identities:

- npm trusted publishing accepts GitHub Actions OIDC and records provenance.
- NuGet trusted publishing exchanges GitHub Actions OIDC for a temporary API key.
- GitHub Releases and GHCR use the job-scoped GitHub token.
- The NuGet username is a non-secret repository variable.
- Release jobs request only the permissions needed for publication.
- Dry-run execution skips every authentication and publication step.

Do not store npm or NuGet publishing tokens in repository or environment
secrets. Registry trusted-publisher settings must bind the expected repository,
workflow, and release environment before the first tag.

## Alternatives Considered

- Store long-lived npm and NuGet tokens as repository secrets. Rejected because
  compromise or accidental disclosure grants authority beyond one workflow run.
- Publish manually from a maintainer workstation. Rejected because it weakens
  reproducibility, provenance, and auditability.
- Use one credential across registries. Rejected because registries expose
  distinct identity and least-privilege boundaries.

## Consequences

- Good: repository storage contains no reusable npm or NuGet publishing secret.
- Good: registry records can identify the originating workflow and source
  revision.
- Good: dry runs validate release preparation without obtaining credentials.
- Bad: maintainers must configure trusted publishers correctly in each external
  registry.
- Bad: publication depends on OIDC and registry availability during a release.

## Confirmation

Release workflow contract tests require OIDC permission, npm provenance,
temporary NuGet credentials, scoped GitHub permissions, and dry-run gates. A
maintainer verifies external trusted-publisher bindings before creating the
first public release tag.

## References

- [Release process](../../../developer/release-process.md).
- [Release security review](../../development/security-review.md).
- [Threat model](../../../developer/threat-model.md).
