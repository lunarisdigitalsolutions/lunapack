## 1. Manifest Contract And Migration

- [ ] 1.1 Replace the `scripts` definition in `pack.schema.json` with event-keyed ordered `hooks` arrays and discriminated script/instruction variants, then verify schema tests accept mixed hooks and instruction-only packs while rejecting unsafe, malformed, empty, and legacy declarations.
- [ ] 1.2 Replace `PackScripts` with ordered typed hook models, update generated YAML registration, path normalization, and model validation, then verify `ManifestSchemaTests` and serialization tests preserve declaration order, normalize both hook file types, and enforce type-specific properties.
- [ ] 1.3 Migrate the bundled `commitlint` pack and all script-based fixtures to `type: script` hook items, then verify every repository pack passes pack validation and no pack manifest retains a top-level `scripts` property.

## 2. Instruction Preparation And Display

- [ ] 2.1 Implement the bounded H2/H3 instruction parser, then verify focused unit tests cover introductions, major and nested numbering, orphan H3 headings, heading-free documents, per-document numbering reset, and literal preservation of non-step lines.
- [ ] 2.2 Implement snapshot-confined strict UTF-8 instruction loading and optional Scriban preparation using resolved node parameters, then verify focused tests cover static content, conditions, current time, Windows path normalization, missing files, traversal, invalid UTF-8, invalid templates, and unknown variables.
- [ ] 2.3 Implement instruction presentation through `CliConsole`, then verify focused tests show introductions and one step at a time with `Press Enter to continue...` interactively, show complete ordered content without reads non-interactively, and add no completion, link, or code-block behavior.

## 3. Ordered Lifecycle Integration

- [ ] 3.1 Generalize lifecycle planning to immutable typed pre/post entries while preserving dependency order, event selection, declaration order, and event-wide `disabledHooks`, then verify planner tests cover mixed hooks, multiple scripts, transient suppression, new update dependencies, and unchanged or removed packs.
- [ ] 3.2 Adapt script authorization to authorize every planned script before dispatch and reassociate authorized commands with ordered entries, then verify trust, decline, `--scripts run`, and `--scripts skip` tests preserve existing security behavior without suppressing instructions.
- [ ] 3.3 Dispatch prepared script and instruction entries inside the existing transaction boundaries, then verify lifecycle tests cover mixed pre/post ordering, script integrity checks, pre-mutation failure, post-mutation failure, interactive cancellation, manifest tamper detection, and rollback before state persistence.
- [ ] 3.4 Add `--skip-instructions` to all install and update command forms and propagate it before instruction loading, then verify command and lifecycle tests prove skipped instructions are neither validated nor displayed while scripts retain their selected consent mode.
- [ ] 3.5 Generalize dry-run output for typed hooks, then verify formatter and command tests report instruction pack, event, file, templating state, and step count after validation without entering guided display or prompting.
- [ ] 3.6 Add install/update integration coverage for static and templated instructions, mixed declared order, transient event suppression, non-interactive output, missing content, and invalid templates, then verify both CLI integration test projects pass those scenarios without unintended project or lock-state mutation.

## 4. Authoring And Inspection

- [ ] 4.1 Replace script-specific authoring commands with append, one-based replace, list, and remove hook commands, then verify `PackAuthoringCommandTests` cover both script forms, instruction templating, ordering, invalid positions, normalized paths, atomic validation failure, and removal of legacy command syntax.
- [ ] 4.2 Replace script-only local formatting and resolved-pack inspection with ordered typed hook output, then verify formatter and inspect tests retain exact script details, show instruction file and effective templating state, report no-hook manifests, and preserve disabled-event output.
- [ ] 4.3 Update CLI help snapshots and contextual next actions for hook commands and lifecycle options, then verify command-help tests contain no obsolete `pack add script`, `pack rm script`, or `pack scripts` guidance.

## 5. Documentation And Governance

- [ ] 5.1 Create the next ADR from the repository template for unified ordered lifecycle hooks, record schema compatibility, authorization, transaction, interaction, and parser decisions, add it to the ADR index, and verify Markdown lint passes.
- [ ] 5.2 Update product and internal lifecycle documentation for typed hooks, instruction boundaries, security, ordering, dry-run, and rollback behavior, then verify references distinguish script execution trust from non-executable instruction display.
- [ ] 5.3 Update developer command, install, update, trust, pack-authoring, and Scriban guidance with exact `scripts`-to-`hooks` YAML and CLI migrations plus interactive, non-interactive, and skip examples, then verify documented commands match CLI help.
- [ ] 5.4 Add the externally observable manifest, command, and instruction behavior to `CHANGELOG.md`, clearly mark the breaking migration, and verify internal-only implementation details remain excluded.

## 6. Final Validation

- [ ] 6.1 Run C# formatting plus Prettier and Markdown lint over changed files, then verify the working diff contains no formatting or documentation diagnostics.
- [ ] 6.2 Run `dotnet test projects/cli/src/Lunapack.Cli.UnitTests/Lunapack.Cli.UnitTests.csproj` and `dotnet test projects/cli/src/Lunapack.Cli.IntegrationTests/Lunapack.Cli.IntegrationTests.csproj`, then verify both suites complete successfully.
- [ ] 6.3 Run `./build.ps1 -Os win -Platform x64`, then verify locked restore and Native AOT publication complete successfully.
