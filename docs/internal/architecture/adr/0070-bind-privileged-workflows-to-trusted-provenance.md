---
status: accepted
date: 2026-08-31
decision-makers:
  - Lunaris Engineering
---

# ADR-0070: Bind Privileged Workflows to Trusted Provenance

## Context and Problem Statement

Release reruns can select artifacts by workflow run ID, pull-request gates load
repository scripts, and website builds execute npm dependencies before Pages
deployment. Privileged jobs must not trust an identifier, script, or build
process without proving its origin.

## Decision Drivers

- Published artifacts must correspond to the selected immutable release tag.
- Pull-request content must not redefine the check that authorizes its merge.
- Dependency installation and static-site generation do not need publication
  credentials.

## Considered Options

- Rely on maintainer review of run IDs and pull-request changes.
- Keep combined jobs and require protected environments for every execution.
- Verify provenance in workflows and isolate privileged publication jobs.

## Decision Outcome

Chosen option: "Verify provenance in workflows and isolate privileged
publication jobs," because authorization must be enforced by automation before
bytes or code cross a publication boundary.

Stable release preparation resolves the selected tag to a commit and accepts
artifacts only from the CLI release workflow at that commit. A previous run
must have succeeded. The external-check gate loads its validator from the base
or default branch and treats skipped or cancelled checks as failures. Website
dependencies build in an unprivileged job; a dependent `github-pages`
environment job alone receives Pages write and OIDC permissions.

### Consequences

- Good, because a run ID cannot substitute unrelated release bytes.
- Good, because pull-request code cannot replace its own gate implementation.
- Good, because compromised build dependencies do not receive deployment
  credentials.
- Bad, because intentionally reusing artifacts requires an exact tag-revision
  match and successful prior run.
- Bad, because intentionally skipped checks require explicit gate policy rather
  than implicit success.

### Confirmation

Workflow contract tests require tag-to-run revision binding, trusted gate
checkout, failure for skipped and cancelled checks, and job-level Pages
permission separation.

## More Information

This decision extends
[ADR-0043](0043-verify-existing-release-assets-on-rerun.md).
