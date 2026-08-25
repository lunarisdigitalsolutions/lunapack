## 1. Manifest Strategy Contract

- [x] 1.1 Extend `pack.schema.json`, `PackManifest`, and manifest parsing with
      optional managed-file `strategy.type` and `strategy.method` values, defaulting
      omitted strategies to `copy/overwrite`.
- [x] 1.2 Add schema and manifest-model tests for valid copy/merge combinations,
      invalid combinations, and backward-compatible omitted strategies.

## 2. Update Planning And Synchronization

- [x] 2.1 Add typed planned update actions that compare prior lock ownership
      with a newly resolved, rendered graph and calculate resulting target hashes.
- [x] 2.2 Implement `copy` update methods, including unique numeric backup
      naming, and strategy-aware additions, changes, and removals.
- [x] 2.3 Implement deterministic `lines`, marker-based `section`, and parsed
      `json` merge methods, including recursive object and structurally distinct
      array behavior.
- [x] 2.4 Extend lifecycle transaction handling to snapshot mixed target
      changes, restore on action/state-save failure, and persist the new requested
      roots and complete lock graph only after success.

  Execution notes: update-mode installation planning may retain an existing
  target owner from an earlier version so strategy planning chooses the action.
  Lifecycle code applies the typed update plan, restores its rollback on an
  action or paired state-save failure, and saves requested roots plus the full
  refreshed lock graph only after actions succeed.

  Execution result: `PackLifecycleService.UpdateAsync` now performs that
  transaction, including target-result hashes for merge and skip actions.
  CSharpier completed and `dotnet test --project
projects/cli/src/LunaPack.Cli.UnitTests/LunaPack.Cli.UnitTests.csproj --no-restore`
  passed 136 tests on 2026-08-16.

- [x] 2.5 Add focused unit tests for every planned action, each merge edge
      case, local-hash drift, lock refresh, and filesystem/state rollback.

## 3. Lifecycle Commands

- [x] 3.1 Add shared available-update selection using configured-source and
      semantic-version precedence, then implement `lunapack outdated` output with
      current and latest versions.
- [x] 3.2 Add `lunapack update [<pack-id>@<version>]`, named explicit/latest
      resolution, update-all selection, no-op/current reporting, and failure
      handling for uninstalled or unavailable roots.
- [x] 3.3 Add `--prompt` for update-all and execute all confirmed updates as
      one resolved graph transaction.
- [x] 3.4 Add `--dry-run` to install and update commands, emitting planned
      actions while preventing file, backup, configuration, and lock mutations.
- [x] 3.5 Add command and integration coverage for outdated reporting, named
      and all updates, prompt accept/decline paths, dry-run invariants, and
      preflight failures.

## 4. Bundled Pack Migration

- [x] 4.1 Add the versioned `gitignore-general` pack with a distinct marked
      `.gitignore` section and section-merge strategy.
- [x] 4.2 Update and version `dotnet-gitignore` for section merge,
      `dotnet-sdk-10` and `dotnet-csharpier-tool` for JSON merge, and `license-mit`
      for copy/overwrite.
- [x] 4.3 Refresh repository `lunapack.yml` and `lunapack-lock.yml` through the
      implemented lifecycle and add integration coverage proving both gitignore
      sections coexist.

## 5. Documentation And Release Evidence

- [x] 5.1 Update the product pack-lifecycle requirements with update, outdated,
      dry-run, and strategy behavior after implementation is complete.
- [x] 5.2 Add developer command references for `update` and `outdated`; update
      install guidance and pack-author documentation for strategies, merge rules,
      and dry-run output.
- [x] 5.3 Add internal lifecycle/update design guidance and create ADR-0018
      from the ADR template; add its accepted decision to the ADR index.
- [x] 5.4 Run CSharpier, schema validation, focused unit tests, CLI integration
      tests, and strict OpenSpec validation; record results in the implementation
      change.

  Execution result on 2026-08-16:

  - `dotnet csharpier format` completed across 90 CLI source and test files.
  - `ManifestSchemaTests` passed 32 tests.
  - `dotnet test --project
projects/cli/src/LunaPack.Cli.UnitTests/LunaPack.Cli.UnitTests.csproj --no-restore`
    passed 160 tests.
  - `dotnet test --project
projects/cli/src/LunaPack.Cli.IntegrationTests/LunaPack.Cli.IntegrationTests.csproj
--no-restore` passed 26 tests.
  - `openspec validate add-pack-update-lifecycle --strict` passed.
  - `git diff --check` passed.
