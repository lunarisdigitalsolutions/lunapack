## Context

See [proposal.md](proposal.md) for motivation and the delta specifications for
behavior. Today, `PackInstallationPlanner` expands source, directory, and glob
selectors into source-path plans, compares those source bytes during adoption,
and `PackLifecycleService` copies the source files before persisting SHA-256
digests. `PackManifest` has only managed-file and composite declarations, while
`ProjectConfiguration` has only source and requested-pack intent. Composite
resolution already produces the complete graph before planner mutation.

The change must preserve graph source precedence, destination behavior,
complete-file ownership, transactional rollback, and version-1 compatibility.
It adds a centrally managed Scriban package and changes source assets into
UTF-8 templates: existing packs continue to work unless their literal content
contains unescaped Scriban delimiters.

## Goals / Non-Goals

**Goals:**

- Resolve all graph parameters deterministically before rendering or lifecycle
  mutation.
- Give templates only typed parameter data and supported Scriban date-time
  functions.
- Apply a small, safe condition language to managed-file declarations.
- Preserve rendered content as the sole ownership and digest basis.
- Keep schema version 1 documents without the new fields valid.

**Non-Goals:**

- Persisting explicit installation parameter values, introducing secret
  management, parameter defaults, interactive prompting, or a synchronization
  workflow.
- Providing arbitrary expression evaluation, custom Scriban functions, file
  access, includes, or code execution from pack templates.
- Applying conditions to individual files selected by a directory or glob;
  a condition selects the complete managed-file declaration.
- Supporting numeric, collection, or object parameter types.

## Decisions

### Model parameters and variables as portable declarations and values

Add a `parameters` mapping to `PackManifest`. A declaration contains `type`,
`required` (default false), and non-empty distinct `values` only for `enum`.
Add a `variables` mapping to `ProjectConfiguration`; values are YAML strings or
booleans. Keep both changes optional in their version-1 schemas and initialize
new projects with an empty variables mapping.

Parameter names use one shared identifier rule so they can be command-line
keys, variable keys, template globals, and condition operands. During a graph
install, merge declarations by name. Identical type, requiredness, and enum
values coalesce; any difference fails rather than selecting an arbitrary pack
owner or silently broadening a contract.

Command-line values split at the first `=`. `bool` accepts only `true` or
`false`; `enum` requires an exact listed string; strings retain the remainder
verbatim. Project variables are not coerced: booleans satisfy only `bool`, and
strings satisfy only `string` or `enum`. This makes YAML type intent visible
and keeps invalid variable configuration from silently generating content.

For an optional unresolved parameter, render `bool` as false and `string` or
`enum` as an empty string. This gives optional variables deterministic template
and condition behavior without adding a second default-value mechanism.

Alternatives rejected: a list of declarations loses direct name lookup;
loosely coercing variables hides errors; and persisting command-line values
would unnecessarily retain potentially sensitive input in project state.

### Use one precedence-ordered binding phase for every resolved graph

After graph resolution and before the installation planner, build the merged
parameter contract, then bind values in this order: explicit repeatable
`--parameter`/`-p`; matching project variables; optional typed empty values.
`--no-variables` removes the second source globally, while repeatable
`--skip-variable <name>` removes named values from only that source. Duplicate
explicit names, unknown names, invalid skipped names, and combining
`--no-variables` with `--skip-variable` are command errors. Required
parameters without a value fail in this phase.

The lifecycle passes the immutable resolved parameter set to planning; it does
not write the values to `lunapack.yml` or `lunapack-lock.yml`. Direct-root metadata
and resolved graph ownership remain unchanged. This keeps one user input able
to satisfy compatible declarations in multiple composite nodes while allowing
different pack graphs to remain isolated.

Alternatives rejected: resolving parameters per node leads to late partial
failure and duplicate prompts; treating variables as higher precedence would
make explicit invocations unable to override repository policy.

### Render bytes before target ownership checks

Replace source-path-only planned files with plans that retain source metadata
for diagnostics plus rendered UTF-8 content bytes. The planner expands
selectors, evaluates the declaration condition once, renders each selected
source, detects intra-graph target conflicts, and compares rendered bytes for
`--adopt-existing`. The lifecycle writes those bytes and records their
SHA-256 digests. False-conditioned declarations produce no plans, files, or
lock records. Existing rollback deletes only files actually written from these
plans.

Create a Scriban renderer with strict variable resolution, a fresh template
context per file, and only resolved parameter globals and Scriban built-ins.
Use Scriban's date-time APIs directly, so templates can format `date.now` as a
current year. Do not expose filesystem, host services, or arbitrary .NET
objects. Invalid UTF-8, parsing errors, and rendering errors are planning
failures before target creation.

Alternatives rejected: copying then templating violates adoption and rollback
semantics; retaining source digests makes uninstall ownership depend on bytes
the consumer never received; using a general Scriban expression for conditions
would give manifest authors a broader language than the contract intends.

### Parse a restricted condition language separately from templates

Add an explicit condition parser and type validator. Its grammar is limited to
boolean identifiers and `!identifier`, string or enum comparisons to quoted
string literals with `==` and `!=`, combined with `&&`, `||`, and parentheses.
The validator resolves every identifier against the merged graph contract and
rejects invalid syntax, undeclared names, and type-incompatible operations.
The resulting expression evaluator receives only resolved typed values.

This creates clear errors independent of Scriban template diagnostics, makes
the allowed operations stable, and prevents conditions from invoking template
features. Conditions on directory or glob declarations apply before expansion,
so all files from that declaration are selected or omitted together.

### Publish a dogfooded MIT license template

Add `projects/packs/license-mit/pack.yml` with a required `companyName` string
parameter and one `LICENSE.md` managed-file template. The template formats the
current year through Scriban date-time support and inserts the company name.
Add `companyName: Lunaris Digital Solutions` under root `lunapack.yml` variables,
install `license-mit`, and regenerate matching lock ownership from rendered
content. The existing root `LICENSE.md` becomes pack-owned after its rendered
content matches exactly.

## Risks / Trade-offs

- [Existing source content resembles Scriban syntax] -> Document escaping and
  add regression tests for existing bundled packs before changing their
  lifecycle path.
- [Rendered output is time-dependent] -> Capture one installation-time clock
  value per operation where needed, assert the current year in tests, and
  record the resulting bytes rather than rerendering during uninstall.
- [Condition or variable errors occur in a dependency] -> Collect and validate
  the complete graph before the planner writes any target.
- [Parameter names collide across composite packs] -> Fail incompatible
  declarations with the pack IDs and conflicting shape in the diagnostic.
- [Scriban offers broader language features than intended] -> Restrict exposed
  globals and keep managed-file conditions in a dedicated parser.

## Migration Plan

1. Extend package management, CLI models, serializers, schemas, schema
   fixtures, and project initialization while retaining valid field-omitting
   version-1 documents.
2. Add graph parameter aggregation, request parsing, strict binding, condition
   validation, and staged Scriban rendering; refactor planner/lifecycle tests
   around rendered bytes and no-mutation failures.
3. Add `license-mit`, root variable configuration, generated lock ownership,
   and end-to-end installation coverage. Existing packs receive regression
   coverage to detect literal delimiter problems.
4. Update `docs/product`, `docs/internal`, and `docs/developer`; create
   ADR-0017 from the template and add it to the ADR index, documenting the
   durable graph-wide binding and rendered-content ownership decision.

Rollback is the existing transaction path: any render, validation, copy, or
state-save failure removes new targets and retains prior configuration and
lock files. Removing the release restores the prior package and manifest
assets; no state-schema migration is needed because new fields are optional.
