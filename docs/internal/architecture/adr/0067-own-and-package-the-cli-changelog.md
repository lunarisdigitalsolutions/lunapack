---
status: accepted
date: 2026-08-31
decision-makers: LunaPack maintainers
---

# ADR-0067: Own And Package The CLI Changelog

## Context and Problem Statement

The repository changelog mixed Luna CLI releases with changes to maintained
packs and documentation. Only the CLI currently shares one release version
across GitHub, npm, and NuGet. Registry packages also omitted the release
history that produced their installed version.

## Decision Drivers

- Keep release notes aligned with the component that owns the version.
- Prevent independently versioned pack changes from appearing as CLI changes.
- Give npm and NuGet consumers release history without visiting the repository.
- Preserve version-specific GitHub Release notes.

## Considered Options

- Keep one repository-root changelog for every component.
- Keep a CLI-owned changelog but publish it only through GitHub Releases.
- Keep a CLI-owned changelog and include it in every CLI registry package.

## Decision Outcome

Chosen option: "Keep a CLI-owned changelog and include it in every CLI registry
package", because changelog ownership then follows version ownership and every
distribution channel exposes the same history.

The canonical source is `projects/cli/CHANGELOG.md`. It contains only
externally observable Luna CLI changes. npm launcher and platform packages, and
NuGet pointer and RID-specific tool packages, include the complete file. GitHub
Release notes continue to extract only the matching version section.

### Consequences

- Good, because pack-package and documentation changes no longer imply a CLI
  release.
- Good, because npm and NuGet package contents expose CLI release history.
- Bad, because each packaged CLI artifact gains a small duplicated text file.

### Confirmation

Distribution tests verify npm package contents and release-note extraction.
NuGet package configuration includes the canonical changelog, and maintainer
guidance points contributors and release automation to the project-local path.

## More Information

This decision extends
[ADR-0038](0038-distribute-luna-through-github-npm-and-nuget.md) with package
documentation ownership. Independently versioned packs should keep release
notes with their own versioned distribution when needed.
