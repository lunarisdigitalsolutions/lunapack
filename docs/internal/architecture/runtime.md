# Runtime Contracts

## Sources And Resolution

Local sources are project-relative directories. Git sources use a URL and may
select a branch or commit and a repository-relative path. Git discovery resolves
one commit before use, caches validated metadata under `.lunapack`, and records the
commit in lock state. Catalog browsing excludes invalid candidate packs instead
of failing a whole reachable source; explicit validation reports a selected
pack's issues. The catalog applies Semantic Version precedence, then configured
source order for ties.

Every configured source normalizes into a source fingerprint: a Git
fingerprint combines a sanitized repository identity, a canonical ref, and a
normalized base path, and a local fingerprint uses a canonical project-relative
path. `lunapack.yml` may contain at most one source per fingerprint; loading,
validating, and writing configuration all reject a duplicate. Adding a Git or
GitHub source resolves a supplied short ref through `git ls-remote` to its
complete form and rejects an ambiguous match before the fingerprint or ref is
persisted.

Pack manifests may declare Git sources under pack-local aliases. After graph
resolution, the external-source planner collects only aliases referenced by
selected managed files, groups equivalent fingerprints, and maps them to an
existing or proposed authoritative workspace source name. Missing groups use
one all-or-nothing consent decision. Materialization resolves each group to an
immutable commit in a private operation directory and shares content for equal
fingerprint-and-commit pairs. External content never becomes a pack catalog and
never supplies lifecycle scripts.

Manifest and lock documents are deserialized into typed CLI models before the
CLI validates required fields, semantic versions, paths, hashes, selector and
strategy combinations, and source provenance. The JSON schemas remain the
published contract reference; runtime validation does not load a reflective
schema engine.

Resolution builds one exact graph from requested roots and composite
references. It rejects unavailable packs, cycles, and conflicting versions
before lifecycle planning. A resolved graph has one version per pack ID.

Link resolution remains outside pack catalogs and dependency graphs. It binds
an exact configured source name, evaluates selectors, and snapshots selected
regular-file bytes before planning. Local snapshots prevent live source changes
from separating hashed bytes from copied bytes. Git snapshots resolve one
immutable commit, enumerate regular blobs without requiring `pack.yml`, and
materialize only selected content.

Git link snapshots use the operating system's user cache, keyed by configured
source identity and commit. Cache metadata and bytes are untrusted: reuse
requires exact identity and commit evidence plus blob-ID verification. Invalid
or incomplete entries are repaired or rejected before project mutation.

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

After pack-specific resolution, pack graphs adapt to managed roots. Links emit
managed roots directly. Explicit owner kinds keep pack and link ownership
separate while one ordinal target map rejects cross-root collisions. Links do
not enter parameter, template, strategy, lifecycle-hook, dependency, or trust
behavior.

Pack lifecycle planning emits immutable typed hooks in dependency-first event
order and manifest declaration order. Instruction files are confined to the
copied operation snapshot, decoded as strict UTF-8, optionally rendered with
resolved parameters, and parsed into literal introductions and H2/H3 steps.
Script arguments render before authorization. Every script is authorized before
the dispatcher executes or displays any prepared hook; instructions never enter
the script trust boundary.

Script policy loads project, project-local user, and global-user denial before
script mode or trust evaluation. Any denial dominates and records every origin
in project, local-user, global-user order. Authorization returns denied-hook
diagnostics without resolving commands. The lifecycle service emits all denial
warnings before dispatching retained instructions or mutating managed files.

## Transactions And Safety

Dry runs perform resolution and preflight without mutation. A transaction
snapshots changed files and restores them if an action or paired state save
fails. Uninstall removes unreachable unchanged copy targets and recorded
section-merge regions, while retaining line and JSON merge targets. Missing
targets generate warnings but do not prevent state cleanup. The invocation-scoped
Spectre.Console boundary writes command output, guided instructions, and
lifecycle diagnostics at a
user-selected minimum level. Info output is plain; verbose, debug, warning, and
error output has colored level prefixes. Long-running catalog and lifecycle
commands show status spinners. Source trust is established by consumer
configuration and preserved through provenance, not by pack manifests.

Version-1 project and user-settings schemas accept optional `deny.scripts` and
optional trust grant collections. Omitted denial and explicit `false` mean no
restriction; acknowledgements remain positive-only. Project and pack
initialization use AOT-registered required-property projections, while later
normal store writes retain canonical full-model serialization.

Pre-mutation hooks run inside the transaction before managed-file changes;
post-mutation hooks run before paired state persistence. Hook failure or guided
instruction cancellation restores managed files and retained project-manifest
bytes. External script effects remain outside rollback. Dry runs validate and
report typed hooks without execution, consent prompts, or guided display.

Approved source additions remain candidate configuration until external Git
resolution, selector expansion, target collision checks, managed-file actions,
and lock cross-references all succeed. State-save failure rolls back managed
targets and discards candidate source configuration. Lock records map each used
pack alias to its workspace source, fingerprint, canonical ref, and commit;
external managed files also record source-relative and effective target paths.

## Cache And Operation Lifetime

Catalog Git sources retain validated metadata under the project `.lunapack`
cache. Pack-defined external content instead uses an access-restricted temporary
workspace per lifecycle or validation operation. One operation reuses a sparse
checkout only when fingerprint and resolved commit both match. Disposal and
every handled failure prepare that workspace for deletion and remove it; no
pack-declared alias or fetched working tree becomes persistent source authority.

## Workflow Guidance

The application-layer next-step advisor classifies a validated workspace from
configured sources and requested root packs. Typed command outcomes select up to
three ordered recommendations; a separate renderer escapes and writes them.
Guidance never executes commands, changes exit codes, or treats dry runs as
completed state transitions.

Source removal normalizes configured and lock-referenced sources into
fingerprints and refuses to remove a source while `lunapack-lock.yml` still
records an installed pack or its external content as a consumer. Once no
consumer remains, removal atomically clears source configuration and project
trust bound to that name; requested roots, immutable lock evidence, and
managed files remain. Source rename atomically replaces the configuration key
together with every trust and lock-file reference in one project-state
transaction, leaving pack-local aliases untouched. State loading therefore
accepts resolved source identities that are no longer configured, while
ordinary state writes continue to validate configured source matching.
Source, rename, and uninstall writes use the narrow unavailable-source path.

Link install, update, uninstall, and forced removal use the same transaction
boundary as managed pack files. Configuration, lock evidence, and file actions
commit together; failed state persistence restores prior bytes. Link uninstall
removes lock ownership and managed files but retains configuration, while forced
removal also deletes configuration. Command-scoped target remapping is applied
after link target mapping so lock evidence retains declared and effective paths.
Existing version-1 project and lock files may omit `links` and load as empty
collections.
