# Tasks

## 1. Offline preflight

- [x] 1.1 Implement structured checks for Git, credential-store capability, project/user state, and profile/cache directories; add unit tests for relevance, status, failure handling, exclusive probe creation, and cleanup, then run the focused unit tests.
- [x] 1.2 Register `luna doctor`, bypass preference resolution, render escaped diagnostics/remediation, and derive its exit code from project blockers; add CLI integration tests for unreadable state, no default network access, and unchanged project state, then run the focused integration tests.
- [x] 1.3 Document offline checks and exit behavior in the developer command/environment reference, add the CLI changelog entry and ADR-0090, update the ADR index, and verify Markdown/link/site checks.

## 2. Opt-in network checks

- [x] 2.1 Implement `luna doctor --network` checks for selected-project Git/OCI sources with non-interactive existing credentials and per-source/overall time limits; add unit tests for source selection, timeout, redaction, and no credential writes, then run the focused unit tests.
- [x] 2.2 Add CLI integration tests for configured remote checks, local-source exclusion, timeout reporting, and continuation after failures; document `--network` and timeout behavior, then run the focused integration tests and developer-site build.

## 3. Final review and validation

- [x] 3.1 Run thorough code and security review of the final diff, fix all actionable findings, and rerun affected tests.
- [x] 3.2 Run the Release solution build, all CLI test suites, formatting/style checks, documentation lint, and strict OpenSpec validation; record each command and result for the final test summary.

## Workflow follow-up

- Archive `environment-preflight-checks` after all tracked tasks pass.
- Commit the completed change, push the current branch, and create a pull request using the repository template with a reference to issue #84 and the exact test results.
