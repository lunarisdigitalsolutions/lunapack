# Configure the Pull Request Check Gate

GitHub repository settings cannot be managed by this pack. Complete these steps
after the workflow files are installed.

## Create the Ready-to-Merge Label

In the repository, open **Issues > Labels** and create a `ready-to-merge` label.
The gate starts when this label is added to a non-draft pull request targeting
`main`.

## Run the Gate Once

Add `ready-to-merge` to an eligible pull request, or start **PR External Check
Gate** from the Actions page and provide a pull request number. Confirm that the
`Validate External Checks` job appears.

## Require the Gate

In the ruleset or branch protection settings for `main`, require the
`Validate External Checks` status check before merging.
