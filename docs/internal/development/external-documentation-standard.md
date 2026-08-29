# External developer documentation standard

Use this standard when changing public LunaPack documentation under
`docs/developer`. It operationalizes
[ADR-0022](../architecture/adr/0022-adopt-audience-local-diataxis-documentation.md)
so users can learn and operate implemented LunaPack behavior without depending
on repository internals.

## Audience and boundary

External developer documentation serves people who install Luna, consume or
author packs, automate public commands, troubleshoot behavior, or look up public
CLI and file contracts.

Keep implementation architecture, internal class names, maintainer release
procedures, repository debugging, and unimplemented plans in `docs/internal` or
`docs/product`. Public examples may contain CLI commands, manifests, templates,
directory layouts, stable output, and CI configuration. They must not require
readers to understand CLI source or tests.

Use sources in this order when facts disagree:

1. Implemented command definitions, validation, and help.
2. Public schemas.
3. Automated public-behavior tests.
4. Existing external documentation.
5. Maintained examples and packs.
6. Product specifications for behavior confirmed as implemented.

Report unresolved disagreement. Never present planned behavior as available.

## Choose one Diataxis purpose

Every page has one dominant purpose:

- **Tutorial:** guides a new user through one working path, starts small, and
  ends with a verifiable result.
- **How-to:** solves one recognizable task, states prerequisites, gives an
  actionable sequence, and links to exhaustive reference.
- **Reference:** describes supported syntax and behavior completely in a
  predictable, scannable form.
- **Explanation:** develops understanding of concepts, guarantees, boundaries,
  or tradeoffs without becoming a procedure.

Index pages orient and route readers; keep them short. Split a page when its
audience, prerequisites, or dominant purpose changes.

## Sequence expertise

Diataxis purpose and reader expertise are separate dimensions. A how-to guide
can be basic or expert; placing every how-to in one flat list does not create a
learning journey.

Organize external documentation in this progression:

1. **Get started:** installation, first source, evaluation, preview, and first
   successful install.
2. **Everyday use:** common updates and customization after initial success.
3. **Advanced use:** ownership recovery, source approval, automation,
   multi-root behavior, and specialized file acquisition.
4. **Authoring:** tutorials first, then core declarations, advanced composition
   or execution, and release work.
5. **Reference and help:** exhaustive lookup, security boundaries, and recovery.

Do not require beginners to understand lock internals, source fingerprints,
dependency graphs, trust scope precedence, or merge algorithms before the first
successful workflow. Link to those details at the decision point. Advanced and
expert pages must state prerequisites and recovery consequences explicitly.

## Organize for discovery

Prefer no more than seven top-level navigation entries and seven direct
children per level. Group related pages when a category grows, but do not add
empty wrappers or deep nesting to satisfy a number mechanically. Most pages
should be reachable within three navigation levels.

Order common beginner journeys before specialized behavior. Keep advanced
composition, templating, remapping, lifecycle execution, and security details
out of the first successful workflow unless required to complete it.

Group options with their command, parameter constraints with parameter fields,
source properties with source types, and template values with template syntax.
Split content when it has a distinct user goal, stable searchable name,
different prerequisites, or enough reference detail to overwhelm a guide.

Every page needs one deliberate discovery path. A page may be link-only when it
is uncommon supporting material and every relevant parent links to it. Record
that choice in the audit; otherwise add it to navigation. Do not publish orphaned
pages.

## Write pages

Open with the page's outcome or scope. Add prerequisites only when needed, then
present the main content. Tutorials and operational guides must show how to
verify success. End with logical next steps when they help.

Write direct, concise English for developers new to LunaPack. Use "you" for
instructions, active voice, consistent public terms, and descriptive headings.
Define LunaPack-specific terms on first use. Distinguish requirements, defaults,
recommendations, and optional behavior. Make warnings name the risk,
consequence, and safer action.

Do not claim security, atomicity, determinism, compatibility, or support without
implemented evidence. Keep one authoritative explanation and link to it instead
of copying detailed rules across pages.

## Build examples

Use the smallest valid example that materially improves understanding. Prefer
consistent pack IDs, versions, sources, and paths across related pages. Explain
placeholders near the example and show expected results when they are not
obvious.

Executable consumer examples must reference only pack IDs present in
`projects/packs`; any explicit version must also exist there. Use maintained
packs whose real parameters, files, hooks, and dependencies exercise the
documented behavior. Do not invent a pack or future version in a consumer
command, because readers must be able to run the example without an unrelated
pack-not-found or version-not-found failure.

Pack-authoring documentation is the only place for synthetic pack IDs. Prefix
them with `example-`, confirm they do not exist in `projects/packs`, and create
them with `luna pack init` before any command references them. Before running
consumer commands, show or directly link to the steps that place the pack in a
configured catalog. A versioning workflow must create every referenced release
before it validates, installs, compares, or updates that release. Keep one
synthetic ID across a connected tutorial sequence when the later page extends
the pack created by the earlier page.

Manifest fragments may use a synthetic ID when their authoring context is
explicit and the fragment is not presented as an immediately runnable consumer
command. Prefer the same `example-*` IDs used by nearby authoring guides.

Do not use ellipses in copyable configuration. Label partial fragments. Avoid
unstable output and internal C# examples. Validate YAML and commands where
repository tooling permits. Include script-execution warnings whenever an
example can run pack-provided code.

## Maintain references

For every public command, document its purpose, syntax, positional arguments,
options and aliases, required values, defaults, allowed values, repeatability,
important interactions, changed files or state, preview behavior, common
failures, defined exit behavior, and a useful example when syntax is not enough.
Compare reference pages with generated help and command definitions.

For every public schema property, document its name, purpose, type, required or
optional status, default, allowed values or format, constraints, relative path
base, precedence or merge behavior, template support, and a minimal example
when useful. Keep exhaustive property tables in reference pages; guides should
show only fields needed for their task.

Feature coverage includes installation methods, commands, schemas, environment
variables, source types, templates, lifecycle behavior, managed-file ownership,
resolution, versioning, security boundaries, platform differences, and
automation behavior. Map new public behavior to its authoritative page in the
same change.

## Review checklist

- [ ] Page has one audience, explicit scope, and dominant Diataxis purpose.
- [ ] Claims trace to implementation, schema, tests, or maintained examples.
- [ ] Prerequisites, defaults, constraints, side effects, and verification are
      present where relevant.
- [ ] Examples use supported public syntax and valid files.
- [ ] Consumer pack references exist at the documented versions under
      `projects/packs`.
- [ ] Synthetic `example-*` packs occur only in authoring workflows and are
      created and registered before consumer commands use them.
- [ ] Security and platform differences are explicit where behavior differs.
- [ ] Detailed rules have one authoritative location.
- [ ] Sidebar placement and inbound links provide deliberate discovery.
- [ ] No broken links, orphaned pages, public-to-internal links, or internal
      implementation details remain.
- [ ] Markdown checks and the Docusaurus production build pass.

## Definition of done

An external documentation change is complete when affected public behavior is
accurately covered at the right Diataxis layer, related navigation remains
bounded and ordered, examples are valid, every changed page is discoverable,
and repository documentation checks pass. Record known implementation or schema
disagreements instead of guessing. Update the documentation audit when a change
materially alters its findings.
