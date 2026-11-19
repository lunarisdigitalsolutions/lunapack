# Tasks

## 1. Update option and removal planning

- [x] 1.1 Thread `--remove-modified` through update option parsing, command
      handling, service, and planner; verify command tests cover default and explicit
      values.
- [x] 1.2 Plan pack and link obsolete removals by normalized effective target
      path, compare current SHA-256 with every prior owner digest, skip
      already-missing targets, preserve modified targets by default, and delete
      modified pack targets only with the override; verify planner tests cover
      unchanged, modified, missing, shared, aliased, and still planned targets.

## 2. Transaction safety

- [x] 2.1 Keep the planning-time target digest or expected absence separate from
      the prior lock digest; revalidate the path and content immediately before
      deletion, then fail and roll back on a mismatch; verify transaction tests
      cover post-planning edits, newly created targets, forced deletion, and
      rollback.

## 3. User-visible output and documentation

- [x] 3.1 Format preserve and override-delete actions in update dry-run output;
      verify formatter tests cover both actions without project mutation.
- [x] 3.2 Warn once per preserved effective path after state persistence and
      ensure preserved pack and link targets lose stale lock ownership; verify
      command and lifecycle tests.
- [x] 3.3 Update `docs/developer/update-packs.md`,
      `docs/developer/cli/commands.md`, `docs/developer/manage-links.md`, and
      `projects/cli/CHANGELOG.md`; verify guidance covers default preservation,
      warning, ownership loss, dry-run, and `--remove-modified`.
- [x] 3.4 Add ADR-0090 from the repository template and index it; verify the
      sequence, status, and index link.

## 4. Integration validation and review

- [x] 4.1 Run focused unit tests, the documented CLI solution build and test,
      CSharpier, .NET style verification, Markdown linting, and the developer
      website production build; record each command and result.
- [x] 4.2 Review the final diff for deletion-path safety, verify
      `ProjectMutationPathSecurity.EnsureNoAliases` still guards deletions, and
      report remaining risks and manual-review points.

## Workflow follow-up

- Archive this issue-specific change after implementation and review.
- Commit with the repository's Conventional Commit format and required
  co-author trailer, then push the feature branch.
- Create a PR using the repository template and reference issue #79.
