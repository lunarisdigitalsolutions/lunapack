# Pull Request Title Validation

The `pull-request-validation.yml` workflow checks non-draft pull requests to
`main` whenever they are opened, reopened, marked ready for review, edited, or
updated with new commits.

It installs Commitlint without changing the repository dependency manifest,
then reads the installed `.commitlintrc.cjs` to validate the pull request
title.

This pack includes the `commitlint` pack, which supplies the configuration and
conventional-title guidance. The workflow itself installs its required Node.js
packages so its validation is not conditional on lifecycle script approval.
