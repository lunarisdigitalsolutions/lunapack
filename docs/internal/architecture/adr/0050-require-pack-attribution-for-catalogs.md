---
status: accepted
date: 2026-08-26
decision-makers: LunaPack maintainers
---

# ADR-0050: Require Pack Attribution for Catalogs

## Context and Problem Statement

LunaPack catalogs make packs available for installation. Manifests without an
author or license cannot provide consumers the attribution and licensing facts
needed to assess a pack. ADR-0049 allowed identity-only manifests during
incremental authoring, which permitted such incomplete packs into discovery and
search results.

## Decision Drivers

- Provide attribution and license information for every catalog candidate.
- Keep manifest validation, authoring, and catalog behavior consistent.
- Make required metadata practical in interactive and automated authoring.

## Considered Options

- Require author and license in every pack manifest.
- Keep metadata optional and filter only catalog output.
- Keep identity-only manifests available until a separate publication step.

## Decision Outcome

Chosen option: "Require author and license in every pack manifest", because
the catalog can rely on one validated manifest contract.

### Consequences

- `author` and `license` are non-empty required properties in `pack.yml`.
- `luna pack init` prompts for missing required values interactively and accepts
  `--author` and `--license` for noninteractive use.
- Discovery and search exclude manifests that fail this contract.
- Existing identity-only manifests must add both fields before catalog use.

### Confirmation

Schema, initialization, catalog, and manifest-store unit tests verify the
required fields and catalog exclusion behavior.

## Pros and Cons of the Options

### Require author and license in every pack manifest

- Good, because consumers receive attribution and licensing information for
  every discoverable pack.
- Bad, because existing incomplete manifests require a metadata update.

### Keep metadata optional and filter only catalog output

- Good, because local drafts remain minimally structured.
- Bad, because manifest validation and catalog eligibility diverge.

### Keep identity-only manifests until publication

- Good, because authors can defer metadata.
- Bad, because publication readiness needs another lifecycle and contract.

## More Information

This decision supersedes
[ADR-0049](0049-separate-pack-authoring-validity-from-publication-readiness.md).
See the [pack manifest reference](../../../developer/packs/reference/manifest.md).
