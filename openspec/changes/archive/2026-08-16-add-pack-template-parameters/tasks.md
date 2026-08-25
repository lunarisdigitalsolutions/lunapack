## 1. Contracts And Configuration

- [x] 1.1 Add centrally managed Scriban dependency and reference it from the
      CLI project.
- [x] 1.2 Extend pack and project configuration models with typed parameter
      declarations, managed-file conditions, and string-or-boolean variables.
- [x] 1.3 Extend `pack.schema.json` and `lunapack.schema.json` with optional,
      backward-compatible parameter, condition, and variable definitions.
- [x] 1.4 Add schema fixtures and focused tests for valid and invalid enums,
      conditions, variables, and field-omitting version-1 documents.
- [x] 1.5 Initialize and round-trip an empty project variables mapping through
      project state storage, with unit coverage.

## 2. Parameter Resolution And Conditions

- [x] 2.1 Extend `install` option parsing and `PackInstallationRequest` for
      repeatable `--parameter`/`-p`, `--no-variables`, and
      `--skip-variable` inputs, including request-shape validation.
- [x] 2.2 Implement graph-wide parameter declaration aggregation and strict
      value binding with explicit-input precedence, variable type checks,
      optional typed empty values, missing-required failures, and incompatible
      composite declaration diagnostics.
- [x] 2.3 Add unit tests for command-line parsing, variable precedence and
      skips, value type validation, shared composite parameters, and no-mutation
      failures.
- [x] 2.4 Implement and test the constrained managed-file condition parser,
      declaration type validation, and evaluation for booleans, string/enum
      equality, negation, conjunction, disjunction, and parentheses.

## 3. Rendered Lifecycle Planning

- [x] 3.1 Implement a strict UTF-8 Scriban renderer that exposes resolved
      parameter globals and date-time support without host filesystem or service
      access; add renderer tests including current-year output and failures.
- [x] 3.2 Refactor planned managed files and `PackInstallationPlanner` to
      select conditions, render source content before adoption checks, and compare
      rendered bytes while preserving target-conflict validation.
- [x] 3.3 Refactor `PackLifecycleService` to write rendered bytes, calculate
      lock digests from rendered content, and retain rollback and uninstall
      protection semantics.
- [x] 3.4 Add planner and lifecycle tests for rendered adoption, false
      conditions, template/condition errors, rollback, unchanged legacy packs,
      and rendered lock-file digests.

## 4. Bundled License Pack

- [x] 4.1 Add the parameterized `license-mit` pack manifest and `LICENSE.md`
      Scriban template using `companyName` and the current year.
- [x] 4.2 Add `companyName: Lunaris Digital Solutions` and the `license-mit`
      root request to repository configuration; regenerate lock ownership and
      adopt the rendered root `LICENSE.md`.
- [x] 4.3 Add catalog, CLI, and composite integration coverage proving
      `license-mit` renders from explicit and project-variable values and that
      shared graph parameters are requested once.

## 5. Documentation And Decision Record

- [x] 5.1 Update pack lifecycle and CLI product requirements for parameters,
      variables, conditions, templating, and the license pack.
- [x] 5.2 Update internal pack-model and lifecycle-management architecture
      guidance, create accepted ADR-0017 from the template, and register it in
      the ADR index.
- [x] 5.3 Update developer manifest, schema, pack-authoring, `init`, and
      `install` documentation with parameter declarations, Scriban templates,
      condition grammar, variable precedence, skip options, and migration notes.

## 6. Validation

- [x] 6.1 Run CSharpier on touched CLI C# files and run focused schema,
      parameter-resolution, planner, lifecycle, and integration tests.
- [x] 6.2 Run full CLI unit and integration suites, OpenSpec strict validation,
      and a repository status review to confirm generated state and documentation
      are intentional.
