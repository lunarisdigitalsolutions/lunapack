# Tasks

## 1. Policy Configuration

- [x] 1.1 Add project lifecycle-script policy fields and validation; extend the version-1 project schema and verify optional-field compatibility, unique variable names, and timeout boundaries with focused model and schema tests.
- [x] 1.2 Add the atomic `<profile>/.lunapack/lifecycle-hook-policy.yml` store for global and canonical project-local user policy; test missing-file defaults, round trips, canonical path isolation, invalid settings, path security, and unchanged `config.yml`.
- [x] 1.3 Document project and user policy configuration in `docs/developer/cli/trust-and-scripts.md` and `docs/developer/cli/environment.md`; add an ADR from the repository template and update its index, verifying the ADR sequence and links.

## 2. Effective Policy and Environment Warnings

- [x] 2.1 Implement the platform-aware baseline and resolver for unioned variable names and the smallest configured timeout; test all three scopes, platform name comparison, default behavior, and that policy never grants script trust or overrides denial.
- [x] 2.2 Warn before launching only authorized, executable script hooks; test that warnings contain names but never a secret sentinel value, that the child still receives its inherited value, and that denied, skipped, condition-excluded, and dry-run hooks emit no warning. Update public and internal lifecycle-safety guidance to state that warnings are not isolation.

## 3. Timeout and Lifecycle Outcomes

- [x] 3.1 Pass caller cancellation through lifecycle execution and implement per-hook deadlines through process exit and captured-output draining; terminate and reap the full process tree, and verify timeout, cancellation precedence, descendant cleanup, and output-pipe cleanup with focused process tests.
- [x] 3.2 Document the per-hook timeout, 300-second default, 1–1,800-second range, strictest-scope behavior, timeout failure/rollback, and cancellation distinction in public and internal lifecycle guidance; update `projects/cli/CHANGELOG.md` and verify documented examples and changelog placement.

## 4. Integration and Review

- [x] 4.1 Run `dotnet test projects/cli/src/Lunapack.slnx` and `dotnet build projects/cli/src/Lunapack.slnx`; record each exact command and result, and resolve failures caused by this change.
- [x] 4.2 Run strict OpenSpec validation and review the complete diff against the security invariants; produce the requested test-results summary and manual-review checklist for allow-list entries, sidecar scoping, timeout semantics, and downgrade compatibility.

## Workflow follow-up

- Archive the completed OpenSpec change and verify the archived result.
- Commit with the required co-author trailer, push the feature branch, inspect the repository pull-request template, and create a pull request referencing #74.
