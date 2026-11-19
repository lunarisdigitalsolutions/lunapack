# Tasks

## 1. Strict audit behavior

- [x] 1.1 Add `--strict` and `--fail-fast` handling and a structured scan of all
  locked pack and link targets; map clean, integrity, usage, and read outcomes
  to exit codes `0`, `2`, and `1`. Add unit tests for matching, missing,
  modified, aggregate, fail-fast, and unreadable cases; verify
  `./test.ps1 unit` passes.
- [ ] 1.2 Add CLI integration tests for strict exit codes, rejecting
  `--fail-fast` without `--strict`, and preserving default report-only behavior;
  verify `./test.ps1 int` passes.
- [ ] 1.3 Update `docs/developer/advanced/audit-and-recover.md`,
  `docs/developer/cli/commands.md`, and `projects/cli/CHANGELOG.md`; create an
  ADR for the exit-code contract and add it to the ADR index. Verify Markdown
  lint, Prettier, and the developer website production build pass.

## 2. Integration validation

- [ ] 2.1 Build the Release CLI solution and run all CLI test suites; verify
  `dotnet build projects/cli/src/Lunapack.slnx --configuration Release --no-restore`
  and `dotnet test --solution projects/cli/src/Lunapack.slnx --configuration Release --no-build --no-restore`
  pass.
- [ ] 2.2 Run strict validation with
  `openspec validate strict-managed-file-integrity-audit --type change --strict`;
  verify it reports no errors or warnings.

## Workflow follow-up

- Commit the completed OpenSpec artifact set as one atomic commit; do not push
  during spec review.
- After manual spec review and a new apply request, commit each completed,
  coherent implementation unit atomically. After implementation validation,
  push the branch and create a PR against `main` referencing issue #76; keep
  this OpenSpec change active during PR review.
- After the user manually reviews the PR, archive the completed change, commit
  the archive/spec sync atomically, and push the update to the PR.
