# Clean Code Principles

These language-neutral principles help teams make changes that remain
understandable, testable, and safe to evolve. Apply language-specific guidance
alongside this reference.

Use these principles to improve a concrete design, not as rigid numerical
targets. A smaller function, fewer arguments, a value object, or polymorphism
is useful only when it makes the code clearer than the simpler alternative.

## General Rules

- Follow the project's established conventions and automated formatters.
- Keep designs simple. Remove accidental complexity before adding an abstraction
  or configuration setting.
- Leave every changed area clearer than it was found: improve nearby names,
  remove dead code, and reduce duplication when it directly lowers change risk.
- Investigate and correct the root cause of a defect. A workaround is acceptable
  only when it is explicitly temporary and records the remaining cause.

## Names And Intent

- Name types, functions, variables, and modules for the domain concept or
  outcome they represent; avoid abbreviations and generic names such as
  `Manager`, `Helper`, `Data`, or `Process`.
- Keep one level of abstraction per function. A caller should read as a concise
  description of the workflow, with detail delegated to well-named operations.
- Prefer direct code over comments that explain what the code already says.
  Write comments only for non-obvious constraints, decisions, or invariants.
- Give compound conditions a named local or predicate when it makes the business
  decision easier to read and test. Do not bury several unrelated checks in one
  conditional expression.
- Use guard clauses for invalid input and failed prerequisites when they keep
  the main workflow direct. Preserve one validation block when readers need to
  understand related failures together.
- Keep LINQ for simple transformations and queries. Use a loop when control
  flow, mutable state, early exit, or error context is clearer when explicit.

## Public Contracts

- Keep public interfaces small and intentional. Remove obsolete paths rather
  than preserving unused compatibility layers.
- Keep configurable policy at the application boundary or composition root;
  pass a narrow, validated configuration to lower-level code.
- Use explanatory variables for complex expressions and encapsulate boundary
  conditions in one named place.
- Prefer positive conditions and make temporal dependencies explicit through
  types, arguments, or method order rather than relying on callers to remember
  an undocumented sequence.

## Small Design

- Implement the smallest design that satisfies the current requirement. Do not
  add configuration, extension points, abstractions, or generality without a
  demonstrated need.
- Introduce an interface when alternate implementations exist or it isolates an
  external effect such as filesystem, process, clock, or user interaction. Use
  the concrete type for deterministic behavior with one implementation.
- Eliminate duplicated knowledge, not merely repeated text. Share a concept
  only when its meaning and rate of change are genuinely the same.
- Keep modules cohesive. A type or file should have one reason to change and
  should not own unrelated policy, I/O, parsing, and presentation concerns.
- Organize source folders by feature, domain boundary, or workflow. Do not make
  top-level folders that collect unrelated technical types such as handlers,
  enums, or models.
- Make invalid states difficult to represent. Validate external input at
  boundaries and preserve invariants within the domain.
- Prefer immutable values and read-only collection contracts when they preserve
  required serialization, ordering, and update workflows. Do not copy
  collections merely to claim immutability.

## Design Boundaries

- Prefer polymorphism or a strategy when an open set of variants would otherwise
  spread conditional logic. Keep a small conditional or switch for a closed,
  local decision when that is simpler.
- Use dependency injection for collaborators with I/O, time, randomness,
  configuration, or external effects. Construct the object graph at a high
  level, not inside domain behavior.
- Follow the Law of Demeter: depend on direct collaborators and avoid long
  navigation chains that expose another type's internal structure.
- Do not make systems over-configurable. Expose a setting only when users need
  a stable, supported choice.
- Isolate threading and asynchronous coordination from domain rules. Minimize
  shared mutable state and document ownership and shutdown behavior.

## Detailed Naming

- Choose descriptive, unambiguous, pronounceable, and searchable names. Use one
  consistent word for one concept and avoid encodings, type prefixes, and noise
  words such as `Info`, `Data`, or `Manager`.
- Name types for concepts and methods for actions or queries. Add context with
  a containing type or module instead of repeating it in every identifier.
- Replace magic numbers and strings with named constants or value objects when
  the value represents a domain rule rather than an obvious local literal.
- Keep functions focused on one outcome and one abstraction level. Prefer a
  small number of descriptive arguments; group related values in a dedicated
  value object when they travel together and express an invariant. Do not group
  unrelated parameters only to shorten a signature.

## Function Shape

- Avoid flag and selector arguments. Expose separate operations or use a
  polymorphic collaborator when callers select genuinely different behavior.
- Separate commands that change state from queries that report state unless the
  combined operation is an established, clearly named domain action.
- Do not hide side effects. A function name and contract must reveal I/O,
  mutation, and externally observable behavior.

## Comments And Source Structure

- Explain intent, a non-obvious constraint, an ambiguous argument, or a
  significant consequence when code alone cannot do so. Keep the comment near
  the code it qualifies.
- Do not add noise, closing-brace comments, journal comments, HTML comments, or
  commented-out code. Remove obsolete comments as part of a change.
- Place related concepts and dependent functions close together. Present the
  public workflow before private implementation detail when practical.
- Declare variables near their first meaningful use, use whitespace to group
  related ideas, and let the formatter control indentation and alignment.

## Objects And Data

- Objects encapsulate behavior and hide their internal structure; data
  structures expose data with little behavior. Do not create hybrids that do
  neither well.
- Keep types cohesive, small in responsibility, and deliberate about instance
  state. Prefer several focused methods over passing code or mode selectors into
  a general-purpose method.
- Use inheritance only for a true substitutable relationship. A base type must
  not depend on its derived types.
- Prefer instance behavior when it uses injected collaborators or encapsulated
  state. Use static functions for pure, stateless operations.
- Keep third-party APIs behind small local adapters where they would otherwise
  leak through multiple layers. Add learning tests when a package boundary has
  subtle or critical behavior.

## Make Failures Useful

- Handle failures where the application can make a meaningful decision;
  otherwise propagate them with enough context to diagnose the cause.
- Do not suppress exceptions, failed operations, or validation errors. Preserve
  the original cause without leaking secrets or internal-only information.
- Prefer explicit results and documented contracts for expected failure paths.
  Reserve exceptional control flow for exceptional conditions.

## Test Code

- Keep tests readable, fast, independent, repeatable, self-validating, and
  timely. Test code has the same maintainability standard as production code.
- Test one behavior or concept per test. Prefer a focused assertion, but use
  multiple assertions when together they establish one observable outcome.
- Test boundary conditions, failure paths, and regressions. Investigate skipped
  or intermittent tests rather than normalizing them.
- Share test setup through domain-specific helpers only when it makes intent
  clearer than local setup.
- Keep representative structured test data in versioned fixture files rather
  than embedding large JSON, YAML, or XML literals in test methods.

## Code Smells

Treat rigidity, fragility, immobility, needless complexity, needless
repetition, and opacity as signals to simplify the affected design. Also watch
for hidden temporal coupling, train-wreck navigation, selector arguments, dead
code, misplaced responsibility, and behavior that is inconsistent with nearby
code. Address the root cause in the smallest safe scope.

## Change Safely

- Keep a change focused on one behavioral goal. Refactor separately when it
  does not directly reduce the risk of the requested change.
- Add or update tests for observable behavior, especially boundary conditions,
  failure paths, and regressions.
- Write a focused failing test before implementation for new or corrected
  behavior, then use the narrowest relevant check while iterating.
- Use measurements, profiling, or production evidence before adding complexity
  for performance. Keep the simpler implementation when the improvement is not
  material.
- Leave the codebase clearer than it was found: remove dead code, misleading
  names, and accidental duplication in the changed area.
