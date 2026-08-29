# External Pull Request Check Gate

The `pull-request-check-gate.yml` workflow confirms that all external checks
for a pull request to `main` have completed successfully. It can run when a
non-draft pull request receives the `ready-to-merge` label, when a check suite
finishes, or from the Actions page with an optional pull request number.

The workflow checks out and invokes `.github/scripts/validate-external-checks.js`.
That script resolves the open pull request, then evaluates its latest check run
and commit-status result for the current head SHA. Duplicate names use their
newest result, while this gate's own job is excluded from its check-run query.

The workflow passes `skippedCheckNameParts` to the validator. Each entry is
matched as a case-insensitive substring of a check-run name or commit-status
context. Matching checks are excluded from the gate, including pending and
failed results. Add or remove entries in the workflow when a check should not
block the pull request.

## Repository setup

Create a `ready-to-merge` repository label, then add it to a non-draft pull
request targeting `main` to run the gate once. In the ruleset or branch
protection settings for `main`, require the resulting `Validate External Checks`
status check before merging.
