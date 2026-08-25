---
status: accepted
date: 2026-08-24
decision-makers: Lunaris Digital Solutions
---

# ADR-0042: Exclude Build Metadata From Release Tags

## Context and Problem Statement

One Luna release version identifies GitHub archives, npm packages, NuGet
packages, and OCI image tags. Semantic Versioning permits build metadata after
`+`, but OCI tags do not. Accepting such a release tag would fail only when the
workflow reaches container publication.

## Decision Drivers

- Validate release coordinates before native builds or publication begin.
- Keep one unchanged version across every distribution channel.
- Preserve prerelease versions and their `next` channel behavior.

## Considered Options

- Reject release tags containing build metadata.
- Remove build metadata only from OCI tags.
- Skip OCI publication for versions containing build metadata.

## Decision Outcome

Chosen option: "Reject release tags containing build metadata", because every
published artifact must retain one exact version. Release tags accept
`v<major>.<minor>.<patch>` with optional prerelease identifiers and reject a
`+<build-metadata>` suffix before the build matrix starts.

### Consequences

- Good, because every accepted release version is also a valid OCI tag.
- Good, because package and image coordinates never encode different versions.
- Bad, because maintainers cannot publish Luna releases with SemVer build
  metadata.

### Confirmation

Distribution contract tests verify that both workflow validation boundaries
reject build metadata and retain the explanatory diagnostic.

## Pros and Cons of the Options

### Reject Release Tags Containing Build Metadata

- Good, because invalid cross-registry versions fail before expensive builds.
- Bad, because it narrows the SemVer forms accepted for releases.

### Remove Build Metadata Only From OCI Tags

- Good, because npm and NuGet could retain the complete version.
- Bad, because one release would have different package and image versions.

### Skip OCI Publication For Versions Containing Build Metadata

- Good, because package publication could continue.
- Bad, because an accepted release would omit an advertised distribution.

## More Information

Related: [ADR-0038](0038-distribute-luna-through-github-npm-and-nuget.md) and
the [CLI release guide](../../development/release-cli.md).
