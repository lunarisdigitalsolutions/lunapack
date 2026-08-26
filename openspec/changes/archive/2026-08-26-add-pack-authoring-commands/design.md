## Context

See [proposal.md](proposal.md) for motivation and the delta specifications for
observable behavior. Pack manifests already have one schema, YAML models, and
validation paths used by catalog and lifecycle operations, but no local
authoring boundary. Current schema rules require attribution, while empty
managed content and references represent the supported incremental starting
point.

The CLI already uses nested noun and verb commands, Spectre.Console output,
System.CommandLine parsing, and `ProjectPath` as its sole project-relative path
authority. Authoring must not couple local edits to catalog resolution,
installation, trust decisions, or lifecycle execution.

## Goals / Non-Goals

**Goals:**

- Reuse one pack model, YAML contract, and validation pipeline across authoring,
  catalog, and lifecycle paths.
- Make each mutation deterministic, schema-valid, path-safe, and atomic.
- Cover the full current manifest contract through composable commands while
  keeping common file and script operations short.
- Preserve existing valid manifests and direct YAML editing.

**Non-Goals:**

- Installing, resolving, publishing, or executing the pack being authored.
- Resolving whether a composite reference exists in configured sources.
- Reformatting arbitrary YAML while preserving comments, anchors, or exact key
  order.
- Supporting invalid draft manifests; every persisted state remains valid.
- Renaming existing consumer commands such as top-level `validate` or `inspect`.

## Decisions

### Add a dedicated local `pack` authoring group

Register `luna pack` separately from top-level catalog and lifecycle commands.
The group owns only the `pack.yml` in the effective workspace. Common mutations
use `add`, `set`, `rm`, and `list`, matching established source, variable, and
remapping verbs. Entity nouns disambiguate scripts, references, parameters, and
tags; `luna pack rm <selector>` remains the short managed-content removal form.

Alternative considered: add authoring behavior to top-level `validate` and
`inspect`. Rejected because those commands select released packs from configured
sources, while authoring operates on one local file.

### Require attribution in initialized manifests

Require `id`, `version`, author, and license in `pack.yml`, while allowing empty
content collections. `luna pack init` prompts for missing required metadata in
interactive terminals and requires options otherwise. Keep optional non-empty
`name` and absolute `homepage` metadata. Existing manifests missing attribution
must add both values before catalog use.

Alternative considered: retain identity-only manifests and filter only catalog
output. Rejected because it splits manifest validity from catalog eligibility.

### Reuse one manifest document service

Place loading, typed mutation, serialization, schema validation, and atomic
replacement behind one application boundary. CLI handlers parse intent and
render outcomes; they do not manipulate YAML nodes directly. Catalog and
lifecycle readers continue consuming the same typed contract.

Each mutation reads once, applies one operation to a candidate, validates the
whole candidate, writes a sibling temporary file, flushes it, and atomically
replaces the destination. Validation or I/O failure leaves the original bytes
untouched. Successful serialization may normalize supported YAML formatting but
must preserve all modeled values.

Alternative considered: patch YAML text in each handler. Rejected because
duplicate handling, type preservation, validation, and crash safety would
diverge across commands.

### Keep initialization prompts at the CLI boundary

Initialization options populate known values first. A terminal interaction
adapter supplies missing required values and the default version only when input
and output are interactive. Redirected execution returns a usage error listing
missing options instead of blocking. The document service receives a complete
candidate and never prompts.

Alternative considered: always prompt for optional metadata. Rejected because
automation must stay deterministic and optional values can be added later with
`set`.

### Map author intent to the existing manifest shape

File, directory, and glob commands create the existing selector-specific
`managedFiles` entries rather than introducing a second `files` property.
Defaults produce source-equals-target entries for files and directories. Glob
commands require or derive a valid target according to documented command help;
advanced selector properties remain explicit options. Script arguments persist
under the existing `arguments` property, and file scripts require a runner.

Composite reference, parameter, tag, metadata, and script commands expose every
value admitted by the schema. Replace operations require explicit intent where
silent overwrite would lose a declaration.

Alternative considered: reshape the manifest to mirror concise command words
such as `files` and `args`. Rejected because it would break existing manifests,
models, and documentation without improving the authoring workflow.

### Normalize filesystem paths through `ProjectPath`

Resolve the requested manifest workspace through existing workspace handling.
Pass every external file, directory, script-file, target, and repository-relative
path through `ProjectPath.NormalizeProjectRelativePath`; use `Normalize` only for
already validated glob syntax and persisted path values. Apply semantic glob
validation after normalization. URLs and opaque identifiers bypass path
normalization.

Alternative considered: replace separators within individual handlers. Rejected
because `ProjectPath` is the repository's canonicalization authority and also
guards rooted and escaping input.

### Validate structure locally without lifecycle side effects

`luna pack validate` uses the same schema and semantic validation as mutations
but never resolves sources, evaluates templates, runs scripts, or modifies trust.
Diagnostics include stable manifest locations suitable for terminal output and
tests.

Alternative considered: reuse top-level catalog `validate` internally.
Rejected because catalog validation begins from a source pack reference and may
perform resolution that local authoring neither needs nor owns.

## Risks / Trade-offs

- [Relaxed schema accepts packs not ready for publication] → Document the
  identity-only state as an authoring stage and keep future publication checks
  separate from schema validity.
- [Typed serialization removes comments or normalizes formatting] → Document
  semantic preservation, show changes before commit through normal version
  control, and avoid claiming round-trip lexical preservation.
- [Many subcommands create an inconsistent surface] → Centralize command
  construction, use established verbs, and test generated help and aliases.
- [Glob targets cannot always be inferred safely] → Derive only unambiguous
  targets and otherwise require an explicit target before mutation.
- [Concurrent writers race between load and replace] → Detect destination
  changes before replacement and fail rather than overwrite newer content.
- [Atomic replacement differs by filesystem] → Use the repository filesystem
  abstraction, same-directory temporary files, and failure-injection tests.

## Migration Plan

1. Relax and extend the pack schema while proving every bundled and test
   manifest remains valid.
2. Introduce the shared local manifest document boundary and atomic persistence
   tests.
3. Add initialization, inspection, validation, and mutation commands in
   capability slices.
4. Add an accepted ADR for incremental manifest validity and update internal,
   product, developer, and command-reference documentation.
5. Add externally observable commands and schema behavior to `CHANGELOG.md`.
6. Roll back command registration and authoring services independently if
   needed; reverting the schema relaxation requires first ensuring no
   identity-only manifests remain.
