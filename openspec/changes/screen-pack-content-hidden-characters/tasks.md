# Tasks

## 1. Shared scanner

- [x] 1.1 Implement the stateless scanner, finding model, strict UTF-8/NUL classification, Unicode severity rules, scalar-based locations, and shared diagnostic formatting; verify focused tests cover invalid UTF-8, binary payloads, CRLF/LF/CR, supplementary scalars, bidi controls, U+200B, other format characters, and valid emoji sequences.

## 2. Lifecycle and audit warning-only screening

- [x] 2.1 Screen final install/update plan content after rendering and merge planning but before hooks or mutations; verify lifecycle tests cover normal and dry-run reporting, effective target paths, and unchanged project state when findings exist.
- [x] 2.2 Screen existing managed targets during audit without changing its exit status or project state; verify audit tests preserve files, configuration, and lock state.
- [x] 2.3 Update install, update, and audit developer guidance plus the CLI changelog for warning-only findings; verify examples and described output match current command behavior.

## 3. Authoring and publication warning-only screening

- [x] 3.1 Screen raw managed sources, including resolved external selections, during `luna pack validate` and `luna publish`; verify tests show warnings do not fail validation or prevent publication during the initial rollout.
- [x] 3.2 Update pack-authoring and publishing guidance plus the CLI changelog for warning-only findings; verify documented commands and output match current command behavior.

## 4. Reviewed high-confidence enforcement

- [x] 4.1 Review warning-only findings from maintained packs and focused fixtures, tune false positives, and confirm valid emoji sequences remain finding-free; verify only reviewed bidi controls and U+200B are eligible blockers.
- [x] 4.2 Block install, update, pack validation, and publication on high-confidence findings before side effects, with `--allow-hidden-characters` preserving findings while allowing continuation; verify tests cover each command, no-side-effect failures, warning non-blocking behavior, and override behavior.
- [x] 4.3 Create and index an ADR for the compatibility and override decision; update developer guidance and the CLI changelog for blocking, override, and exit behavior; verify the ADR is indexed and all four command help surfaces expose the override.

## 5. Integration checks

- [x] 5.1 Run `dotnet build projects/cli/src/Lunapack.slnx`, `dotnet test projects/cli/src/Lunapack.slnx`, `openspec validate "screen-pack-content-hidden-characters" --type change --strict`, and `openspec status --change "screen-pack-content-hidden-characters"`; verify build/tests pass and every required artifact is complete.

## Workflow follow-up

- Commit each verified implementation phase atomically, then push the branch and open a pull request referencing #80; do not merge.
- After the user's manual pull-request review, archive this OpenSpec change in a separate commit and verify its archived status.
