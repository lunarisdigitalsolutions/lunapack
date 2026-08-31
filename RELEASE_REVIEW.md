# Release Readiness Review

This ledger coordinates LunaPack's first public-release review. Repository
behavior, tests, schemas, maintained packs, and release automation remain the
authority for every claim. Publication is outside this review.

## Executive Summary

Review in progress. Current status is `NOT READY` until the full validation
matrix passes, public-exposure checks are complete, package installation paths
are smoke-tested, and external publisher settings receive maintainer review.

## Repository Inventory

| Area                             | Primary location                                                                 | Review state                         |
| -------------------------------- | -------------------------------------------------------------------------------- | ------------------------------------ |
| CLI, solution, and tests         | `projects/cli/src`                                                               | In progress                          |
| Public configuration schemas     | `projects/schema`                                                                | Pending final validation             |
| Maintained packs                 | `projects/packs`                                                                 | Pending final validation             |
| Public documentation             | `README.md`, `docs/developer`                                                    | In progress                          |
| Product documentation            | `docs/product`                                                                   | Pending consistency review           |
| Internal documentation and ADRs  | `docs/internal`                                                                  | In progress                          |
| Website                          | `projects/website`                                                               | Pending final build and claim review |
| Build and release automation     | `.github/workflows`, `.github/actions`                                           | In progress                          |
| NuGet, npm, and OCI distribution | `projects/cli/distribution`                                                      | In progress                          |
| Community and governance         | Root community files, `.github`                                                  | Pending final review                 |
| Security model and review        | `docs/developer/threat-model.md`, `docs/internal/development/security-review.md` | In progress                          |

## Execution Plan

1. Reconcile public, product, and internal documentation with implemented
   behavior; remove sensitive, stale, generated, or internal-only material.
2. Review accepted ADRs against current architecture without rewriting decision
   history; add a new ADR only for a durable undocumented decision.
3. Review CLI architecture, public commands, file formats, diagnostics, and
   cross-platform behavior; make focused changes with matching tests.
4. Reconcile website claims and maintained packs with the validated public
   interface.
5. Validate workflows, actions, NuGet, npm, and OCI staging without publishing.
6. Complete STRIDE review, remediate scoped release blockers, and record
   residual risks with verification methods.
7. Verify open-source community files, licensing metadata, dependency policy,
   and generated-file hygiene.
8. Run final builds, tests, analyzers, formatters, schema and link checks,
   package creation, distribution smoke tests, and sensitive-data scans.
9. Reinspect the final diff and replace this plan with completed evidence,
   remaining risks, maintainer decisions, and the exact readiness status.

## Current Release Gates

- Non-Windows NuGet and npm installation paths still need final smoke tests.
- Trusted-publisher and registry settings require maintainer verification
  outside the working tree.
- OCI build and smoke validation require an available Docker backend.
- Final package, workflow, dependency, secret, and public-history scans remain
  outstanding.
- The current working tree contains pre-existing review changes that must be
  validated together and checked for accidental generated artifacts.

## Decisions and Assumptions

- Existing uncommitted work is preserved and treated as part of this review.
- The least disruptive behavior wins where repository evidence is incomplete.
- No package, image, release, or website will be published by this review.
- Legally significant license, trademark, and ownership claims remain subject
  to maintainer review.

## Validation Log

Validation results will be recorded here as each gate completes.

## Final Release Readiness Status

NOT READY
