# Tasks

## 1. Manifest and configuration schemas

- [x] 1.1 Add optional `directInstallable` metadata with a `true` default to
  the pack schema and typed manifest model; test omission, explicit `true` and
  `false`, and rejection of non-Boolean values with
  `dotnet test projects/cli/src/Lunapack.slnx`.
- [x] 1.2 Add optional project and user-settings preference schemas and typed
  models without a schema-version bump; test missing preferences, valid and
  invalid values, and preservation of existing trust data with
  `dotnet test projects/cli/src/Lunapack.slnx`.
- [x] 1.3 Document `directInstallable`, its default, and its non-security
  compatibility limit in `docs/developer/packs/reference/manifest.md`; verify
  examples against the schema tests.
- [x] 1.4 Leave existing pack manifests directly installable unless a
  maintainer separately approves a candidate; verify omitted metadata keeps
  current packs eligible in `dotnet test projects/cli/src/Lunapack.slnx`.

## 2. Scoped preferences and workflow guidance

- [x] 2.1 Implement typed preference storage and one resolver for project-local,
  user-project, user-global, and built-in values; test precedence, defaults,
  canonical project keys, and explicit `false` values with
  `dotnet test projects/cli/src/Lunapack.slnx`.
- [x] 2.2 Add `luna config set|list|remove` with mutually exclusive
  `--local`, `--user-project`, and `--global` options; test selected-scope
  behavior, global default, invalid keys and values, conflicting scopes, and
  trust-data preservation with `dotnet test projects/cli/src/Lunapack.slnx`.
- [x] 2.3 Apply `next-steps.enabled` to shared guidance output while preserving
  `--suppress-next-steps` as an invocation override; test enabled-by-default,
  configured suppression, and unchanged primary command results with
  `dotnet test projects/cli/src/Lunapack.slnx`.
- [x] 2.4 Document preference keys, YAML shape, scope flags, precedence, and
  examples in `docs/developer/cli/configuration.md`; verify examples against
  schema and command tests.

## 3. Catalog filtering and inspection

- [x] 3.1 Apply shared direct-installability and composite predicates to
  discover and search, add `--only-composites`, and filter before version
  selection and relevance ranking; test both command defaults, composite-only
  behavior, dependency-only exclusion with `--allow-draft`, and older eligible
  release fallback with `dotnet test projects/cli/src/Lunapack.slnx`.
- [x] 3.2 Keep complete-catalog access for validation and explicit inspection;
  show the inspected pack's direct-installability state and preserve search
  link results; test dependency-only inspection, validation, and unchanged link
  results with `dotnet test projects/cli/src/Lunapack.slnx`.
- [x] 3.3 Document raw/composite definitions and discover/search behavior in
  `docs/developer/cli/commands.md` and
  `docs/developer/packs/reference/catalog-layout.md`; verify documented
  options against CLI help and command tests.

## 4. Direct-root lifecycle and release guidance

- [x] 4.1 Restrict install, update, and outdated root selection to
  directly-installable releases while leaving dependency resolution
  unrestricted; test explicit and latest-version rejection, older eligible
  fallback, and dependency-only composite resolution with
  `dotnet test projects/cli/src/Lunapack.slnx`.
- [x] 4.2 Update `docs/developer/install-packs.md`,
  `docs/developer/update-packs.md`, and
  `docs/developer/packs/explanation/composition-and-lifecycle.md` to explain
  direct roots versus dependencies; verify examples against lifecycle tests.
- [x] 4.3 Add ADR-0084 from the ADR template documenting installability and
  catalog-resolution boundaries, preference precedence, and compatibility
  limits; add it to `docs/internal/architecture/adr/index.md` and verify the
  index link and required ADR sections.
- [x] 4.4 Add one externally observable CLI entry to
  `projects/cli/CHANGELOG.md` covering manifest eligibility, catalog filters,
  scoped preferences, and next-step control; verify it follows the current
  changelog format and excludes internal-only changes.

## 5. Integration validation

- [x] 5.1 Run `dotnet build projects/cli/src/Lunapack.slnx` and
  `dotnet test projects/cli/src/Lunapack.slnx`; confirm all focused and
  integration tests pass.
- [x] 5.2 Run OpenSpec validation for `configure-catalog-discovery`; confirm
  proposal, all five capability specs, design, and tasks validate together.
