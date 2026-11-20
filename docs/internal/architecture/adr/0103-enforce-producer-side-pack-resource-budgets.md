---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0103: Enforce Producer-Side Pack Resource Budgets

## Context and Problem Statement

Project documents, pack manifests, source snapshots, dependency graphs, selected
files, templates, and OCI archives cross from repository- or registry-controlled
data into Luna's process and temporary storage. Reading those inputs completely
before checking size, or checking only in a later planner, can exhaust memory,
disk, or processing time before Luna rejects the operation.

Luna also supports project-local, user-project, and global preferences. A
repository-controlled project must not be able to raise its own resource
allowance for other users, while a user may need explicit finite increases for
an unusually large trusted pack.

## Decision Drivers

- Bound input reads and processing before parsing, list growth, rendering, or
  workspace mutation.
- Preserve version-1 YAML compatibility and existing preference precedence.
- Prevent project-owned settings from increasing resource consumption.
- Allow explicit user-owned increases within finite hard ceilings.
- Preserve OCI path, digest, duplicate-entry, and link protections.
- Report useful size and count diagnostics without exposing pack content.

## Considered Options

- Use compiled limits that cannot be configured.
- Resolve limits once but enforce them only in lifecycle planning.
- Resolve scoped limits once and enforce them at every producer boundary.

## Decision Outcome

Chosen option: "Resolve scoped limits once and enforce them at every producer
boundary," because planner-only checks leave parser, enumeration, copy, and
rendering allocations unbounded.

Luna resolves the user-owned baseline from user-project, global-user, and
built-in order, then applies a project-local value only as a lower cap.
User-owned values may raise or lower a default, but never exceed a documented
finite hard ceiling. A project value cannot raise either the default or a
stricter user-owned baseline. Zero, negative, non-integer, overflowing, and
unknown values fail without changing settings. Missing values retain existing
behavior and require no schema migration.

Compiled bootstrap ceilings protect project and user settings before those
documents can supply overrides. Effective limits then flow through bounded
manifest and pack-document reads, local and Git source enumeration, snapshot
copying, shared graph traversal, selected-file expansion, Scriban output, OCI
download and extraction, OCI publication, and Luna Link materialization. File
limits count regular files and directories. Byte checks count actual stream
output; count checks stop before an over-limit list is retained. Graph depth,
unique nodes, traversed references, and catalog work share budgets across all
requested roots. Lock YAML collection shapes are preflighted before typed
deserialization so pack, reference, link, and managed-file lists cannot
amplify the byte-bounded document into an unbounded object graph.

Compiled safeguards cover intermediate producers that the configurable
per-object and count limits do not fully describe. Git and external-source path
enumeration is capped at 64 MiB of output and 1 MiB per record, and retained
catalog manifests and OCI repository/tag page payloads are capped at 64 MiB in
aggregate. OCI pagination also has a finite page count derived from the
effective record limit. Git cache entries remeasure canonical embedded
manifests and reject forged byte counts or aggregate overrun before catalog
reuse. OCI archive file limits count regular files, explicit directories, and
implicit parent directories. These ceilings cannot be raised through
configuration.

Limit failures report the operation, observed value or lower bound, configured
limit, and unit. They return before managed-file, configuration, or lock
mutation. Existing path confinement, digest verification, archive-entry
validation, source identity, trust, and transaction controls remain independent
and cannot be raised away by resource preferences.

### Consequences

- Good, because hostile input is rejected at the producer that would otherwise
  allocate or copy it.
- Good, because project repositories can lower risk locally without granting
  themselves a larger resource allowance.
- Good, because trusted users can raise finite limits for exceptional packs
  without rebuilding Luna.
- Good, because one effective policy keeps local, Git, and OCI behavior aligned.
- Bad, because unusually large existing inputs may now require an explicit
  user-owned override.
- Bad, because every new parser, enumerator, renderer, or materializer becomes
  a producer boundary that must adopt the shared policy.
- Bad, because older Luna versions can ignore newly persisted optional
  preferences and therefore do not enforce their lower values.

### Confirmation

Unit and security tests verify exact boundaries and limit-plus-one rejection
for bytes, files, graph depth, nodes, references, rendered output, and OCI
artifacts. Failure-preservation tests verify that configuration, lock state,
workspace files, and extraction roots remain unchanged. Schema tests bind
project maxima and user hard ceilings to the runtime definitions. The final
security review must verify that no producer bypasses the shared policy.

## Pros and Cons of the Options

### Use compiled limits that cannot be configured

- Good, because every operation uses one simple immutable policy.
- Bad, because exceptional trusted packs cannot be used without a rebuild.

### Enforce limits only in lifecycle planning

- Good, because it centralizes graph and output validation.
- Bad, because parsers, process output, file enumeration, copies, and template
  execution can exhaust resources before the planner runs.

### Resolve scoped limits and enforce them at producers

- Good, because checks bound allocations and mutations where each resource is
  produced.
- Good, because repository and user trust boundaries remain distinct.
- Bad, because limit propagation and tests span several subsystems.

## More Information

This decision implements
[issue #83](https://github.com/lunarisdigitalsolutions/lunapack/issues/83).
Consumer configuration is documented in
[CLI configuration](../../../developer/cli/configuration.md). It follows
[ADR-0071](0071-reject-filesystem-aliases-at-mutation-boundaries.md) for
independent filesystem confinement and
[ADR-0084](0084-use-oci-distribution-artifacts-for-pack-releases.md) for OCI
integrity boundaries.
