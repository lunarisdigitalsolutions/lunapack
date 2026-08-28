## 1. Resolved Target Context

- [x] 1.1 Add planner tests for condition-selected concrete target expansion,
      including remapped file targets, directory/glob-derived declared targets,
      forward references, and ambiguous declared targets.
- [x] 1.2 Introduce immutable managed-file template context and diagnostic models
      that contain only the current effective target and selected declared-to-effective
      target lookup.
- [x] 1.3 Refactor `PackInstallationPlanner` into target expansion/resolution and
      rendering/preflight phases while preserving selector order, ownership checks,
      strategies, and existing failure behavior.

## 2. Scriban Path Functions

- [x] 2.1 Add `PackTemplateRenderer` tests for `files.path` with remapped targets
      and `files.relative_path` with remapped current and referenced targets.
- [x] 2.2 Add renderer tests for `/` normalization, root-level current targets,
      missing and ambiguous fallback values, recorded diagnostics, and absence of the
      `files` object from instruction and script-argument rendering contexts.
- [x] 2.3 Implement the read-only managed-file `files` Scriban object using only
      resolved-plan data and `ProjectPath` normalization, with no filesystem service
      exposed to template code.
- [x] 2.4 Return successful rendered content with unresolved-reference diagnostics
      and aggregate them on `PackInstallationPlan` without converting warnings into
      planning failures.

## 3. Lifecycle Integration

- [x] 3.1 Emit successful-plan template diagnostics through `CliConsole.Warning`
      with the unresolved declared target and current effective rendering target.
- [x] 3.2 Add lifecycle tests proving install, update, and dry-run render identical
      effective and relative paths and emit fallback warnings without mutation or
      non-success results.
- [x] 3.3 Add regression coverage proving conditionally excluded references return
      their original declared targets and that existing parameter, instruction, and
      script-argument template behavior remains unchanged.

## 4. Documentation

- [x] 4.1 Create ADR-0057 from the repository template for the resolved-plan-only
      Scriban path API and add it to the ADR index.
- [x] 4.2 Update `docs/internal/development/path-handling.md` and related lifecycle
      guidance with the two-phase target map, normalization authority, warning flow,
      and no-filesystem boundary.
- [x] 4.3 Update `docs/developer/parameters-and-variables.md` and relevant pack
      manifest guidance with `files.path`, `files.relative_path`, remapping examples,
      conditional/missing fallback behavior, and portable separator guarantees.
- [x] 4.4 Update `docs/product/prd/003-pack-lifecycle.md` with the managed-file
      template resolution contract and add the externally observable capability to
      `CHANGELOG.md`.

## 5. Verification

- [x] 5.1 Format changed C# and Markdown files and run focused renderer, planner,
      and lifecycle tests.
- [ ] 5.2 Run the complete CLI test suite and locked restore validation.
- [x] 5.3 Publish the CLI with Native AOT for the current runtime and verify no new
      warnings or trimming failures.
