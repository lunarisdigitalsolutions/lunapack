# Adding Packages

Use a third-party package when it delivers a well-maintained capability more
reliably, securely, or efficiently than repository-owned code. Prefer the
platform library first; do not reimplement a mature capability merely to avoid
a dependency.

## Required Evidence

Record the following evidence in the change:

- The package solves a concrete requirement better than a small, maintainable
  in-repository implementation. State the alternatives considered.
- Its license is approved by maintainer policy.
- It is actively maintained: its repository has a recent release or meaningful
  maintenance activity within the last 24 months, supports the repository's
  target framework, and has an identified maintainer or organization.
- It has healthy adoption: general-purpose packages normally have at least
  5,000 weekly downloads from the relevant registry. A lower-volume
  domain-specific or first-party package requires a documented justification.
- Its source, release history, security advisories, and transitive dependencies
  have been reviewed. Reject abandoned packages, unexplained binaries, or
  dependencies with unresolved high-severity vulnerabilities.
- The selected stable release is compatible with the target framework and does
  not duplicate an existing dependency's capability.

## .NET Workflow

For .NET packages, declare the reference only in the owning project and add its
exact version to central package management. Do not set package versions in
individual project files. Restore the affected project, review the direct and
transitive package graph, and commit changed package lock files.

Validate the affected build and tests after restoring. Keep the version current
through routine dependency review, and revisit the dependency when its
maintenance, license, security posture, or target-framework support changes.
