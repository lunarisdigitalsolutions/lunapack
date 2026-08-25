# Runtime Contracts

## Sources And Resolution

Local sources are project-relative directories. Git sources use a URL and may
select a branch or commit and a repository-relative path. Git discovery resolves
one commit before use, caches validated metadata under `.lunapack`, and records the
commit in lock state. Catalog browsing excludes invalid candidate packs instead
of failing a whole reachable source; explicit validation reports a selected
pack's issues. The catalog applies Semantic Version precedence, then configured
source order for ties.

Manifest and lock documents are deserialized into typed CLI models before the
CLI validates required fields, semantic versions, paths, hashes, selector and
strategy combinations, and source provenance. The JSON schemas remain the
published contract reference; runtime validation does not load a reflective
schema engine.

Resolution builds one exact graph from requested roots and composite
references. It rejects unavailable packs, cycles, and conflicting versions
before lifecycle planning. A resolved graph has one version per pack ID.

## Planning And Ownership

Install and update resolve the full graph, bind parameters, evaluate managed
file conditions, render managed files only when their manifest selector opts
into UTF-8 Scriban templates, and preflight target actions.
Root-nearest parameter declarations control requiredness and enum values; all
same-name declarations retain their type. Composite bindings set hidden
transient values unless a root declares the parameter.

The planner supports copy, backup, skip, and merge actions. Shared targets
require merge strategies for every owner. The lock document records final
rendered bytes as SHA-256 digests.

## Transactions And Safety

Dry runs perform resolution and preflight without mutation. A transaction
snapshots changed files and restores them if an action or paired state save
fails. Uninstall removes unreachable unchanged copy targets and recorded
section-merge regions, while retaining line and JSON merge targets. Missing
targets generate warnings but do not prevent state cleanup. The invocation-scoped
Spectre.Console boundary writes command output and lifecycle diagnostics at a
user-selected minimum level. Info output is plain; verbose, debug, warning, and
error output has colored level prefixes. Long-running catalog and lifecycle
commands show status spinners. Source trust is established by consumer
configuration and preserved through provenance, not by pack manifests.

## Workflow Guidance

The application-layer next-step advisor classifies a validated workspace from
configured sources and requested root packs. Typed command outcomes select up to
three ordered recommendations; a separate renderer escapes and writes them.
Guidance never executes commands, changes exit codes, or treats dry runs as
completed state transitions.

Source removal atomically removes source configuration and project trust bound
to that source name. Requested roots, immutable lock evidence, and managed files
remain. State loading therefore accepts resolved source identities that are no
longer configured, while ordinary state writes continue to validate configured
source matching. Source and uninstall writes use the narrow unavailable-source
path.
