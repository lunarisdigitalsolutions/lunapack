# Tasks

## 1. Configuration and policy

- [x] 1.1 Add finite typed resource-limit defaults and scoped `luna config` overrides; verify project-only reductions, user-scope increases, precedence, invalid values, and unchanged Boolean preferences with focused tests.
- [x] 1.2 Bound project and user settings bootstrap reads before parsing; verify oversized settings fail before deserialization and cannot change the active configuration.
- [x] 1.3 Document resource-limit keys, units, defaults, scopes, and bootstrap ceilings in developer guidance; verify every documented value matches the implementation.

## 2. Bounded input and source materialization

- [x] 2.1 Enforce byte-counted limits before parsing project/pack manifests, lock files, and pack documents; verify exact-limit acceptance, limit-plus-one rejection, lower-bound diagnostics, and unchanged project state with focused tests.
- [x] 2.2 Enforce pack snapshot byte and file-count limits during source materialization/enumeration; verify local, Git-backed, and OCI-backed boundary behavior without materializing over-limit file lists.

## 3. Graph, selection, and rendering budgets

- [x] 3.1 Enforce shared graph depth, unique-node, and traversed-reference budgets across all requested roots; verify boundary, duplicate-edge, cycle, and over-limit cases.
- [x] 3.2 Bound file selection before list materialization and template output while produced; verify per-pack/graph counts, per-file/cumulative rendered bytes, and no workspace or lock mutation on rejection.

## 4. Archive, architecture, and release documentation

- [x] 4.1 Wire effective resource limits into OCI archive reads and extraction; verify existing path, digest, duplicate-entry, and link protections still reject unsafe entries when size limits are raised.
- [x] 4.2 Update internal architecture/threat-model guidance, add an ADR and index entry, and add the observable CLI behavior to the changelog; verify links, ADR numbering, and documented defaults.

## 5. Integration and security verification

- [x] 5.1 Run focused tests, the CLI solution build and test suites, and local install/update/validation boundary scenarios; record exact commands and results for the user.
- [x] 5.2 Complete a security review of the final diff, fix all actionable findings, and verify the review reports no unresolved high-confidence issues.

## Workflow follow-up

- Archive the OpenSpec change after implementation and verification are complete.
- Commit the completed change, push the feature branch, and open a pull request referencing issue #83.
