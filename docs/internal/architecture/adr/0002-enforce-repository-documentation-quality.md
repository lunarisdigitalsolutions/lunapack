---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0002: Enforce Repository Documentation Quality

## Context and Problem Statement

The repository is documentation-first and includes Markdown, structured
configuration, and generated HTML. Consistent formatting and linting must be
inexpensive to run locally and must apply before changes are committed.

## Decision Drivers

- Detect documentation defects before a change is committed.
- Use format-specific tooling for Markdown and structured files.
- Keep local quality checks inexpensive and repeatable.

## Considered Options

- Use Markdownlint, Prettier, and pre-commit checks.
- Rely on manual formatting review.
- Use one formatter for every file type.

## Decision Outcome

Chosen option: "Use Markdownlint, Prettier, and pre-commit checks", because
the repository needs format-specific, automated feedback before commits.

### Consequences

- Good, because contributors receive formatting and lint feedback before
  committing.
- Good, because Markdown and structured files use tools suited to their formats.
- Bad, because tooling versions and behavior must be maintained at the
  repository root.

### Confirmation

The `lint:docs` command validates Markdown, and the Husky pre-commit hook runs
the configured staged-file checks.

## Pros and Cons of the Options

### Use Markdownlint, Prettier, and pre-commit checks

- Good, because quality feedback is automated and format-specific.
- Bad, because contributors depend on repository-managed tooling.

### Rely on manual formatting review

- Good, because it introduces no local tooling.
- Bad, because inconsistencies would be found late and unevenly.

### Use one formatter for every file type

- Good, because contributors would use a single tool.
- Bad, because Markdown and structured files need different tools.

## More Information

Use repository-level documentation quality gates:

- Use Markdownlint for `README.md` and Markdown under `docs/`.
- Use Prettier for JSON, CSS, SCSS, HTML, YAML, and YML files.
- Configure Prettier with `prettier-config-standard`.
- Run the configured checks through `lint-staged` from the Husky pre-commit hook.
- Provide `lint:docs` for full Markdown checks.
- Provide `format:docs` and `format:prettier` for manual fixes.

- [Package scripts and staged-file configuration](../../../../package.json)
- [Prettier configuration](../../../../prettier.config.cjs)
- [Husky pre-commit hook](../../../../.husky/pre-commit)
- [Repository README](../../../../README.md)
