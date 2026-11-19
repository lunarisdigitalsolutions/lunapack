# Tasks

## 1. Canonical Skill and Pack

- [ ] 1.1 Write the portable `luna-cli` `SKILL.md` in the versioned pack target
  with supported commands, examples, and approval boundaries; verify frontmatter
  and command syntax against Luna CLI help and public documentation.
- [ ] 1.2 Add the versioned `luna-cli` pack manifest with the shared identity,
  `.agents/skills/luna-cli` target, and `copy/fail-if-exists`; verify
  `luna pack validate` and catalog validation for `luna-cli@1.0.0`.
- [ ] 1.3 Smoke-test direct Skills CLI and LunaPack installs in separate clean
  fixtures; verify both yield the same identity and target, and verify an
  occupied target remains unchanged until removed through its current owner.

## 2. Public Guidance and Decision Record

- [ ] 2.1 Add a focused developer how-to for both installation routes, project
  ownership and switching, and safe Luna CLI workflows; verify examples and
  links against current commands and documented behavior.
- [ ] 2.2 Add an accepted ADR for the canonical identity and one-installer
  ownership rule using the next sequence number; update the ADR index and verify
  its link and Markdown lint.
- [ ] 2.3 Add the developer page to site navigation; run documentation lint and
  the documented Docusaurus production build.

## Workflow follow-up

- Commit these OpenSpec artifacts atomically on the feature branch; do not push
  until implementation is complete.
- After user review and approval of these OpenSpec artifacts, start
  implementation.
- After implementation and validation, commit completed logical units
  atomically, then push the branch and create a PR using the repository
  template with a reference to #102. Keep this OpenSpec change active through
  PR review.
- After user confirms PR review is complete, archive this change, verify the
  archived status, commit the archive atomically, and push the update.
