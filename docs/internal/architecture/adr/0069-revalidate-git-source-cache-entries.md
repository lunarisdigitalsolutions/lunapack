---
status: accepted
date: 2026-08-31
decision-makers:
  - Lunaris Engineering
---

# ADR-0069: Revalidate Git Source Cache Entries

## Context and Problem Statement

Git discovery caches parsed pack metadata under the consuming project. That
directory is repository content and can be changed independently of the Git
revision represented by an entry. Deserialization and a matching source
fingerprint alone do not preserve the validation performed during discovery.

## Decision Drivers

- A project-local cache is untrusted derived state.
- Cache hits must enforce the same manifest and path invariants as fresh input.
- Invalid cache content should cause rediscovery, not fail an otherwise usable
  source.

## Considered Options

- Trust cache files whose source fingerprint matches.
- Sign cache entries with a machine-local key.
- Revalidate every cache entry before use and treat invalid entries as misses.

## Decision Outcome

Chosen option: "Revalidate every cache entry before use and treat invalid
entries as misses," because it restores domain invariants without introducing
key storage or making cache state authoritative.

Cache loading validates its version, source fingerprint, immutable commit,
canonical repository-relative pack path, semantic version, manifest structure,
and agreement between cached identity fields and the embedded manifest. Any
failure discards the complete entry.

### Consequences

- Good, because modified cache content cannot bypass manifest validation.
- Good, because deletion or corruption still degrades to normal rediscovery.
- Bad, because cache hits repeat inexpensive model validation.

### Confirmation

Unit tests must reject invalid cached manifests, escaping pack paths, mismatched
pack identities, malformed versions, and source fingerprint mismatches while
accepting valid entries.

## More Information

This decision refines [ADR-0019](0019-use-installed-git-for-pack-sources.md).
