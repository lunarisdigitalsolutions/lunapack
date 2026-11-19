# Tasks

## 1. Shared JSON Output Contract

- [x] 1.1 Add the command-scoped `--format text|json` option, v1 envelope DTOs,
  and serializer; verify focused tests for command names, camel-case fields,
  empty arrays, and nullable values.
- [x] 1.2 Route JSON-mode console output to the invocation error writer and
  serialize complete envelopes once; verify CLI tests for parseable stdout,
  stderr diagnostics, text as default, and unsupported-format errors.

## 2. Audit JSON Diagnostics

- [x] 2.1 Build the audit DTO from pack, link, external-source, and external-file
  data before writing output; verify structured result tests cover stable
  fields, status tokens, ordering, and failure without partial JSON.
- [x] 2.2 Map non-current audit states to stable codes, severities, and subjects,
  then compute summary counts; verify every spec mapping, file paths, owning
  pack identity, and zero-filled severity counts with focused tests.

## 3. Outdated and Validation JSON

- [x] 3.1 Add the outdated result DTO, including `remoteRefsChecked`; verify
  tests cover offline output, empty and populated arrays, OCI integrity data,
  and unchanged exit codes.
- [x] 3.2 Add the validate result DTO and JSON failure path; verify tests cover
  valid and invalid packs, stderr issues, null results on operation failure,
  and unchanged exit codes.
- [x] 3.3 Update `docs/developer/cli/commands.md` and
  `docs/developer/advanced/audit-and-recover.md` with format usage, result
  fields, audit codes/statuses, stream behavior, and compatibility policy;
  verify examples match CLI tests and Markdown quality checks pass.
- [x] 3.4 Add accepted ADR-0090 from the repository template and index it;
  verify its link, status, and sequential number.
- [x] 3.5 Add an externally observable CLI entry under `Unreleased` in
  `projects/cli/CHANGELOG.md`; verify it documents only the new consumer-facing
  behavior.

## 4. Integration Verification

- [x] 4.1 Run `dotnet build projects/cli/src/Lunapack.slnx` and
  `dotnet test projects/cli/src/Lunapack.slnx`; verify both complete
  successfully.
- [x] 4.2 Run `openspec validate stable-machine-readable-cli-diagnostics`;
  verify all change artifacts pass validation.

## Workflow follow-up

- Commit implementation in atomic changes, push the branch, and open a PR for
  #75 using the repository template and a Conventional Commit title.
- Wait for the user's manual PR review. Only after review, archive the OpenSpec
  change and commit and push that archive separately.
