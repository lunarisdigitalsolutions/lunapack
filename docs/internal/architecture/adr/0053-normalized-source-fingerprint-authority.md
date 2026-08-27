---
status: accepted
date: 2026-08-26
decision-makers: LunaPack maintainers
---

# ADR-0053: Normalize Source Identity Into a Single Fingerprint Authority

## Context and Problem Statement

Pack manifests can declare pack-local aliases for external Git content,
alongside the local and Git sources a consumer names in `lunapack.yml`. The
same repository can appear as an HTTPS URL, an `ssh://` URL, or scp-style
shorthand, with or without a `.git` suffix, and a ref can be short (`main`) or
complete (`refs/heads/main`). `ConfiguredSourceIdentity` compared mostly
literal URL and ref values, so equivalent sources under different transports,
casing, or ref forms were not recognized as equal, and nothing mapped a
pack-declared alias onto a workspace source a consumer had already configured.

A resolved pack graph can also require several sources at once; approving each
dependency's source individually risks repeated prompts and partially updated
project state. This decision also supersedes
[ADR-0047](0047-retain-lock-evidence-after-source-removal.md), which allowed
removing a source even while installed lock state referenced it, on the
assumption that a source's only remaining role was historical provenance. Once
a source can keep feeding external content that `update`, `outdated`, and
`audit` continue to resolve, an unavailable source would silently break those
workflows instead of leaving inert evidence.

## Decision Drivers

- Recognize equivalent Git repositories regardless of transport, casing, or
  trailing syntax.
- Let a pack-local alias resolve to an already-configured workspace source
  instead of requiring a duplicate per pack.
- Approve every source a resolved graph needs in one reviewable decision
  instead of prompting per dependency.
- Keep a workspace source available while it still backs an installed pack's
  external content, audit, or update behavior.
- Keep `rm` canonical per
  [ADR-0048](0048-use-rm-for-configuration-removal-commands.md) while
  accepting the `remove` wording already used in existing prose.

## Considered Options

- Normalize every source declaration into one source fingerprint and route
  identity, alias mapping, consent, and removal safety through it.
- Compare sources through existing literal `ConfiguredSourceIdentity` equality.
- Persist pack-to-workspace alias mappings directly in `lunapack.yml`.
- Keep ADR-0047's unconditional removal.

## Decision Outcome

Chosen option: "Normalize every source declaration into one source
fingerprint", because it gives every source-aware operation one authority for
identity instead of repeating ad hoc comparisons.

`SourceIdentityNormalizer` derives a `SourceFingerprint` for every configured
source. A Git fingerprint combines a sanitized repository identity, canonical
ref, and normalized base path
(`git:{host/owner/repo}@{canonical-ref}#{normalized-base-path}`); a local
fingerprint uses a canonical project-relative path (`local:{canonical-path}`).
Supported HTTPS, `ssh://`, and scp-style forms of one repository share an
identity; embedded credentials are rejected; hosts are lowercased; owner and
repository segments are lowercased only for `github.com`; a `.git` suffix and
trailing separators are stripped; base paths reject `..` escapes. A short
branch or tag resolves through `git ls-remote` to its complete ref before it is
persisted, and an ambiguous match is rejected. `lunapack.yml` may contain at
most one source per fingerprint; every load, validate, and write path rejects
a duplicate. This fingerprint, not a configured name or literal URL, is the
authority the CLI already uses for duplicate detection, Git and GitHub
source-addition canonicalization, Git cache keys, and pack-alias mapping. A pack
therefore does not force a second configured entry for a repository a consumer
already added.

Before install or update mutates a project, LunaPack collects the
external-source declarations a resolved graph's managed files select, group
them by fingerprint, and match each group to a configured source or a
proposed new identifier. Every missing source surfaces together in one
sanitized, all-or-nothing approval prompt that defaults to no.
`--accept-sources` approves only conflict-free additions, without
bypassing path safety, script trust, or transactionality.

`luna sources rm <name>` now inspects `lunapack-lock.yml` and refuses removal
while an installed pack or its external content still references that source.
`remove` remains a compatibility alias of the canonical `rm` verb from
ADR-0048, because ADR-0047 and the pack-authoring examples that motivated this
change both used `remove` in prose. `luna sources rename` atomically updates
the configuration key plus every trust and lock-file reference in one
transaction, without touching pack manifests.

### Consequences

- Good, because equivalent repositories under different transports, casing, or
  ref shorthand collapse to one workspace source and cache entry.
- Good, because a pack's declared source never forces a second workspace entry
  for an already-configured repository.
- Good, because a missing-source decision covers the whole graph at once
  instead of drifting across per-dependency prompts.
- Good, because a source that still backs installed external content, audit,
  or update evidence cannot be removed out from under those workflows.
- Bad, because removing a consumed source now requires uninstalling or moving
  its consumers first.
- Bad, because ref canonicalization adds a remote `git ls-remote` round trip
  before a source can be added or matched.

### Confirmation

Source-identity, Git ref-resolution, workspace source-command, and lifecycle
unit tests cover HTTPS/SSH/scp equivalence, GitHub case folding, credential
rejection, duplicate-fingerprint rejection, ambiguous-ref rejection, atomic
rename, removal refusal with a lock consumer, graph-wide approval,
`--accept-sources`, materialization, provenance, and rollback. See the
[pack-defined external Git sources change](../../../../openspec/changes/pack-defined-external-git-sources/design.md).

## Pros and Cons of the Options

### Normalize every source declaration into one source fingerprint

- Good, because every source-aware operation shares one identity and equality
  contract.
- Bad, because the normalizer must keep pace with every provider-specific URL
  and ref form the CLI accepts.

### Compare sources through literal `ConfiguredSourceIdentity` equality

- Good, because it requires no new value object or normalization service.
- Bad, because equivalent sources under different transports, casing, or short
  refs would keep comparing as distinct.

### Persist pack-to-workspace alias mappings in `lunapack.yml`

- Good, because a mapping would not need to be recomputed during resolution.
- Bad, because mappings are installation evidence, not portable configuration.

### Keep ADR-0047's unconditional removal

- Good, because it avoids a behavior change to an existing command.
- Bad, because a consumed source could be removed while update, outdated, or
  audit still depend on it resolving.

## More Information

This decision supersedes
[ADR-0047](0047-retain-lock-evidence-after-source-removal.md); its
trust-revocation and lock-retention behavior is unchanged for a source that
removal still permits. It applies
[ADR-0048](0048-use-rm-for-configuration-removal-commands.md)'s naming
convention to the newly requested pack-level source-removal command. See the
[pack-defined external Git sources design](../../../../openspec/changes/pack-defined-external-git-sources/design.md),
[lifecycle script safety](../../development/lifecycle-script-safety.md), and
[add a pack source](../../../developer/sources.md).
