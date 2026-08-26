# Branch naming

Choose a concise branch name that signals the work category and formats its descriptive portion in camelCase.

## Branch prefixes

- `features/<description>` for new capabilities. Pair it with the `feat` Conventional Commit type.
- `bugfixes/<description>` for defect corrections. Pair it with the `fix` Conventional Commit type.
- `ci/<description>` for CI, GitHub Actions, pipeline, or infrastructure automation work. Pair it with the
  `ci` Conventional Commit type.
- `docs/<description>` for documentation work. Pair it with the `docs` Conventional Commit type.

Leave the Conventional Commit scope out of the branch name because the prefix already identifies the change type.
Make the camelCase description brief, specific, and recognizable in pull requests.

The branch prefix communicates the change purpose, while the pull request title must still comply with the full
[Conventional Commits](./conventional-commits.md) specification.
