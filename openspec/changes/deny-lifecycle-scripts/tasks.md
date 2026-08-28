# Deny Lifecycle Scripts Tasks

## 1. Trust Schema And Models

- [x] 1.1 Extend the version-1 project schema with optional `trust.deny.scripts`, make project trust grant collections optional, and add schema tests for denial-only, omitted, false, and existing empty-collection configurations.
- [x] 1.2 Extend the user-settings schema with optional global-user and project-local `deny.scripts`, optional grant collections, and positive-only acknowledgements; add cross-platform schema tests for valid denial scopes and invalid acknowledgement denial.
- [x] 1.3 Add runtime denial models that keep effective trust scopes separate from positive project acknowledgements, normalize omitted values to false and omitted grants to empty collections, and register all shapes for Native AOT YAML serialization.
- [x] 1.4 Update model validation and settings/project-store tests to cover denial-only documents, explicit false compatibility, omitted defaults, and round trips that preserve unrelated grants and settings.

## 2. Trust Commands And Persistence

- [x] 2.1 Add trust-service operations to set and reset script denial in project-local user, portable project, and global-user scopes while preserving grants, unrelated settings, and atomic-write behavior.
- [x] 2.2 Add `luna trust scripts deny` with existing scope selection rules, no confirmation, no project acknowledgement for portable denial, and idempotent persistence.
- [x] 2.3 Add `luna trust scripts reset` with a retained-grant danger warning, interactive confirmation, fail-closed noninteractive behavior, and removal limited to the selected scope.
- [x] 2.4 Extend trust listings and output formatting to report denial independently from retained grants with stable `project`, `local-user`, and `global-user` scope names.
- [x] 2.5 Add command and service tests for all three scopes, mutually exclusive options, idempotency, confirmation outcomes, project rollback on settings failure, grant preservation, no acknowledgement creation, and denial/list/reset output.

## 3. Lifecycle Denial Enforcement

- [x] 3.1 Add an effective script-policy evaluator that returns every active denial origin in deterministic project, local-user, global-user order and treats any origin as dominant.
- [x] 3.2 Refactor lifecycle authorization to evaluate denial before every script mode, grant lookup, executable resolution, or confirmation; retain instructions and return denied-hook diagnostics while authorizing no scripts.
- [x] 3.3 Add warning presentation that emits one pre-mutation warning per denied hook with pack ID, version, lifecycle event, and all denying scopes.
- [x] 3.4 Extend dry-run planning and formatting to load effective denial, report `policy-denied` with all origins, and avoid execution-denial warnings or prompts.
- [x] 3.5 Add focused authorization tests proving denial overrides `--scripts run`, prompt mode, explicit skip mode, source trust, pack trust, and multiple simultaneous scopes without resolving commands or invoking confirmers.
- [x] 3.6 Add lifecycle tests across pre/post install, update, and uninstall hooks proving warnings precede all hook processing and mutation, denied scripts never execute, instructions remain ordered, no-script plans remain quiet, and lifecycle state still completes.
- [x] 3.7 Add CLI process coverage for project, local-user, and global-user denial, warning origin text, retained grants, dry-run output, and non-bypass through `--scripts run`.

## 4. Minimal Initialization YAML

- [x] 4.1 Add structured initialization serialization for `luna init` that writes only `schemaVersion`, required empty `sources` and `packs`, and lock `schemaVersion` plus required empty `packs`.
- [x] 4.2 Add project initialization tests asserting exact minimal YAML shape, published-schema validity, refusal to overwrite either file, and successful loading through normal project stores.
- [x] 4.3 Add structured initialization serialization for `luna pack init` that retains required `author`, `id`, `license`, and `version` values while omitting every optional null, empty, or default-valued property.
- [x] 4.4 Add pack initialization tests for option and interactive defaults, exact minimal YAML shape, published-schema validity, existing-file protection, and later loading and mutation through the normal manifest store.

## 5. Documentation And Validation

- [x] 5.1 Create and index an accepted ADR for dominant script denial, monotonic scope composition, retained grants, warning timing, and the decision to continue lifecycle work without denied scripts.
- [x] 5.2 Update `docs/product/prd/001-mvp.md`, `003-pack-lifecycle.md`, and `004-cli.md` with persistent deny policy, precedence, scope management, warning behavior, and minimal initialization output where relevant.
- [x] 5.3 Update internal runtime, lifecycle-script safety, schema, and initialization guidance with data shapes, authorization order, reset risk, compatibility, and testing expectations.
- [x] 5.4 Update developer CLI commands, configuration, trust-and-scripts, lifecycle-hooks, installation, update, uninstall, troubleshooting, and pack-authoring guidance with commands, YAML examples, defaults, warnings, dry-run behavior, and minimal init output.
- [x] 5.5 Add an externally observable entry to `CHANGELOG.md` covering persistent script denial, non-bypass semantics, warning origins, and minimal generated YAML.
- [ ] 5.6 Run focused schema, trust, lifecycle authorization, initialization, and CLI process tests; then run the complete CLI test suite, Native AOT publish validation, OpenSpec strict validation, and Markdown/link checks.
