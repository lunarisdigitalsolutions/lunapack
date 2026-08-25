---
status: accepted
date: 2026-08-24
decision-makers: Lunaris Digital Solutions
---

# ADR-0043: Verify Existing Release Assets on Rerun

## Context and Problem Statement

A release workflow can stop after creating a GitHub Release but before all
registries publish. Rerunning must preserve immutable release artifacts without
treating an incomplete or changed GitHub Release as valid.

## Decision Drivers

- Resume interrupted registry publication safely.
- Keep release archives, checksums, and notes immutable for one tag.
- Fail closed when remote release state differs from rebuilt local state.

## Considered Options

- Trust any existing GitHub Release for the requested tag.
- Replace existing release assets during every rerun.
- Verify existing release assets and notes before continuing.

## Decision Outcome

Chosen option: "Verify existing release assets and notes before continuing",
because immutable release state must match the same source rebuild before later
publication phases resume.

When the GitHub Release exists, the workflow downloads all uploaded assets. It
requires exactly the five native archives, `SHA256SUMS.txt`, and `CHANGELOG.md`,
then byte-compares each with local staging. It also compares the release body
with the staged changelog section. Any missing, unexpected, or changed content
fails the rerun before npm, NuGet, or OCI publication.

### Consequences

- Good, because an interrupted release can resume without replacing immutable
  assets.
- Good, because incomplete or divergent release state cannot silently pass.
- Bad, because maintainers cannot attach extra files before the automated
  release workflow completes.

### Confirmation

Distribution tests cover matching, missing, unexpected, and byte-different
asset sets. Workflow contract tests require asset download, verification, and
release-note comparison before package publication.

## Pros and Cons of the Options

### Trust Any Existing GitHub Release

- Good, because reruns are simple.
- Bad, because partial uploads can be mistaken for a complete release.

### Replace Existing Release Assets During Every Rerun

- Good, because remote state converges automatically.
- Bad, because published artifacts cease to be immutable.

### Verify Existing Release Assets and Notes

- Good, because reruns preserve and prove immutable state.
- Bad, because intentional post-release asset additions require a separate
  governed process.

## More Information

Related: [ADR-0038](0038-distribute-luna-through-github-npm-and-nuget.md) and
the [CLI release guide](../../development/release-cli.md).
