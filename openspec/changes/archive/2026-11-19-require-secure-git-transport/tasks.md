# Tasks

## 1. Scoped Preference and Schemas

- [x] 1.1 Add `git.allow-insecure-transport` to the typed preference model,
      `luna config` key handling, persistence, and project > user-project > global
      resolution; verify default `false`, explicit-`false` override, and all-scope
      set/list/remove behavior with focused preference tests.
- [x] 1.2 Extend project and user-settings JSON Schemas with
      `preferences.git.allowInsecureTransport`; verify Boolean acceptance,
      non-Boolean rejection, and compatibility for existing version-1 files with
      schema tests.
- [x] 1.3 Document the key, scope precedence, default, and project/global
      examples in developer guidance; verify examples match the implemented CLI
      syntax.

## 2. Git Transport Policy

- [x] 2.1 Enforce the effective preference before network access in source
      addition, ref resolution, catalog discovery, pack and external-source
      materialization, Git-backed links, and pack authoring; verify insecure
      requests fail before network access or state mutation while HTTPS, SSH,
      scp-style, and local sources remain unaffected.
- [x] 2.2 Add Git-process protocol restrictions that prevent secure-source
      operations from being rewritten to HTTP/Git; verify `url.*.insteadOf` and
      redirect behavior with local Git integration tests.
- [x] 2.3 Add one sanitized warning per opted-in insecure source per CLI
      operation and suppress Git credential helpers for opted-in HTTP; verify
      warning deduplication, helper non-invocation, and absence of URL credentials
      from output and logs.
- [x] 2.4 Add focused source, credential, and identity regression tests; update
      developer and internal guidance with opt-in risks and residual Git-config
      credential paths, add ADR-0101 and its index entry, and update the CLI
      changelog; verify docs, ADR links, and the changelog describe implemented
      behavior.
- [x] 2.5 Complete a security-focused code review and fix every actionable
      finding; verify each fix with a regression test or documented evidence.

## 3. Integration Verification

- [x] 3.1 Run the CLI solution build and test suites; verify all focused and
      integration tests pass.
- [x] 3.2 Validate the OpenSpec change and all updated schemas; verify the
      change is ready to archive and no schema or spec validation errors remain.

## Workflow follow-up

- Archive only `require-secure-git-transport` after implementation and
  verification are complete; verify the archived result.
- Commit with a Conventional Commit title and required co-author trailer, then
  push the feature branch.
- Create a pull request referencing #78 and following the repository template;
  include test results and a manual-review checklist.
