# Conventional Pull Request Titles

This pack uses Conventional Commits to keep pull request titles readable and
release-note tooling predictable. The title is validated; individual commit
messages remain a team choice.

## Title Format

Use `type(scope): Subject`, with scope optional. For example:

```text
feat(frontend): Add pack audit output
fix: Handle missing local source
docs(backend): Clarify pack installation
```

Titles must use sentence case, stay within 144 characters, and name one of the
supported types:

- `build` for build tooling or dependencies
- `ci` for continuous-integration configuration
- `docs` for documentation-only changes
- `feat` for product capabilities
- `fix` for defect corrections
- `revert` for reversing a prior change
- `release` for a release title

Scopes are optional. When present, choose `frontend`, or `backend`.
Append `!` after the type or scope when the change is breaking, such as
`feat(cli)!: Change the pack manifest format`.

The installed `.commitlintrc.cjs` contains the exact enforcement rules. Its
post-install hook adds the Commitlint CLI and conventional configuration as
development dependencies.
