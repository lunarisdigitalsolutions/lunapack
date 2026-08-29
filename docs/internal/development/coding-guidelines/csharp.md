# C# Coding Guidelines

Pair the language-neutral clean-code principles with these
C#-specific rules.

## Language And Dependencies

- Use file-scoped namespaces, nullable reference types, .NET implicit usings,
  and the current supported C# language version. Add file-local `using`
  directives for other namespaces; do not add repository-defined global
  usings.
- Name public and internal APIs for domain intent. Prefer precise immutable
  records or sealed types where mutation or inheritance is not required.
- Name methods and properties with PascalCase. Name private fields with an
  underscore-prefixed camel-case identifier. Follow the repository formatter
  and analyzer configuration for enforceable C# styling rules.

## Structure And Layout

- Place each type under its narrowest owning feature and make its namespace
  match its directory, as established by
  [ADR-0061](../../architecture/adr/0061-organize-cli-source-by-feature.md).
  Avoid catch-all `Interfaces`, `Models`, and `Helpers` directories.
- Keep methods focused and short enough to expose their workflow. Extract a
  private method for a distinct decision, validation, or side effect rather
  than hiding it in a long method. Keep enforceable size limits in analyzer
  configuration rather than duplicating numeric limits in guidance.
- Place fields first, then the public workflow, and then its private detail.
  Keep callers above their dependent private methods where practical.
- Use `StringComparison.Ordinal` or `OrdinalIgnoreCase` for identifiers,
  paths, hashes, and protocol values unless user-facing linguistic comparison is
  explicitly required.
- Let the selected formatter own layout. Do not hand-format source against its
  output.
- Prefer primary constructors when constructor parameters directly establish a
  type's dependencies or immutable state.
- Prefer records for simple immutable DTOs and POCOs. Use required members,
  constructors, or nullable properties for incomplete deserialization state;
  do not use `string.Empty` as a placeholder default.
- Only one type per file is allowed.

## Application Boundaries

- Isolate filesystem access behind an injected dependency when it must be
  tested, simulated, or replaced. Keep direct filesystem calls at the boundary
  instead of spreading them through domain behavior.
- Keep parsing, command orchestration, domain operations, persistence, and
  console presentation in separate, cohesive types. Keep `Program` as the
  composition root.
- Keep configuration and concrete object construction at the composition root.
  Inject direct collaborators; do not create I/O, clock, or configuration
  dependencies inside domain methods.
- Require an explicit non-null `CliConsole` for components that emit CLI output.
  Pass the invocation console from runtime composition and a silent, no-color
  `IAnsiConsole` through `CliConsole` in unit tests; do not add optional output
  fallbacks or direct `Console` calls.
- Use a mature command-line parser for CLI syntax, help, and input validation.
  Keep command handlers thin and delegate application behavior to testable
  services.

## Type And Package Boundaries

- Prefer a strategy or polymorphic type when an expanding family of behaviors
  would repeat conditionals. Retain a small switch for a closed local mapping
  when it is clearer than an abstraction.
- Use a `record` or sealed value type for related values that share a domain
  invariant. Do not pass Boolean flags or loosely related primitives to select
  different behavior.
- Keep external package APIs behind a narrow owner when their types or behavior
  would otherwise spread across application layers.

## Input And Serialization

- Validate external input at the boundary. Represent expected operation failures
  explicitly; add context when propagating unexpected exceptions.
- Model optional values with nullable types and narrow them through pattern
  matching or guard clauses. Use nullable flow attributes such as
  `NotNullWhen` when a Boolean result establishes a contract. Do not use the
  null-forgiving operator to consume result values or hide an unproven
  invariant.
- Use `ProjectPath.Normalize` for stored project-relative values and
  `NormalizeProjectRelativePath` for external filesystem locations, following
  [ADR-0037](../../architecture/adr/0037-canonicalize-persisted-project-paths.md).
  Do not normalize URLs or opaque identifiers as paths.
- Define shared `JsonSerializerOptions` once at the narrowest common boundary;
  do not create divergent defaults in each serializer user.
- Choose a package when it is demonstrably safer or clearer than bespoke code;
  assess its maintenance, licensing, and compatibility before adoption.

## Quality

- Keep target-framework, compiler, analyzer, formatter, and warning policy in
  shared build configuration. Treat warnings as errors.
- Fix a diagnostic when code can express the intended contract. When an
  external compatibility constraint requires suppression, use the narrowest
  `SuppressMessage` scope and include a justification that references an issue
  or ADR. Do not use `NoWarn`, warning pragmas, or analyzer severity reductions
  for source exceptions.
- Centralize dependency versions where the build system supports it. Commit
  dependency locks when reproducible restore matters, and restore in locked
  mode in continuous integration.
- Preserve schema compatibility when validating serialized contracts. Keep the
  selected validator behind a small boundary when its API would otherwise leak
  across the application.
- Diagnose failures to their root cause. Do not suppress an analyzer, test, or
  runtime failure with a workaround unless the exception and follow-up are
  explicit and narrowly scoped.
- Collection initialization can be simplified and should be used when possible.

## Performance And Memory

- Start with correct, clear code and profile or benchmark representative inputs
  before optimizing. Include the measured workload and result in the change
  when an optimization adds complexity.
- Avoid unnecessary allocations on measured hot paths: pre-size collections
  when the final count is known, avoid materializing intermediate LINQ results,
  and use ordinal string operations.
- Stream files and data that may be large. Dispose streams promptly with
  `using`; use asynchronous I/O only when the caller can await it end to end.
  Do not block on asynchronous work in normal application flows.
- Prefer asynchronous APIs when the caller can await them. A synchronous bridge
  to an asynchronous library is acceptable only when its synchronous API cannot
  preserve required behavior and the boundary itself is synchronous.
- Use `ReadOnlySpan<T>`, `ArrayPool<T>`, `stackalloc`, `FrozenDictionary`, or
  `ValueTask` only after evidence identifies a suitable hot path. Preserve
  ownership rules for pooled buffers and avoid escaping spans or stack memory.
- Prefer bounded, linear algorithms and explicit limits for externally supplied
  collections, paths, and serialized content. Do not trade correctness,
  readability, or security for unmeasured micro-optimizations.

## Test Code

- Name tests `Scenario_Condition_ExpectedOutcome`; do not use Arrange-Act-Assert
  narration comments.
- Use injected filesystem doubles for unit tests where appropriate. Run
  integration tests against a real filesystem and label them so they can be
  selected independently.
- Test one behavior per test. Prefer one focused assertion; use multiple
  assertions only when they establish one observable outcome such as preserved
  state after a failed operation.
- Test observable success and failure behavior, including parser validation,
  filesystem failures, and state preservation. Keep test fixtures isolated and
  dispose temporary resources.
- Store substantial structured inputs in test-data files copied to test output.
  Pair success coverage with boundary and negative cases, and use focused
  test-first changes to drive behavior.

## Required Checks

Before merging, run the selected formatter, solution build, relevant test
suite, documentation linting, and link validation. Restore project-local tools
before using a pinned formatter or pre-commit hook.
