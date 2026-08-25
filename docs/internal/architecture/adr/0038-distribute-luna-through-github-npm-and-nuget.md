---
status: accepted
date: 2026-08-21
decision-makers: LunaPack maintainers
---

# ADR-0038: Distribute Luna Through GitHub, npm, And NuGet

## Context and Problem Statement

Luna previously distributed Linux and Windows x64 archives only. Luna now needs
native artifacts for Windows x64, Linux x64 and Arm64, and macOS x64 and Arm64,
plus package-manager installation without mixing NuGet metadata into the CLI
executable project.

## Decision Drivers

- Build every native artifact on a matching operating-system and architecture
  runner.
- Give Node consumers only the binary matching their platform.
- Provide one discoverable .NET tool package.
- Use SDK-supported platform selection for .NET tools.

## Considered Options

- Publish GitHub archives only.
- Publish one package containing every native binary.
- Publish platform npm packages and RID-specific NuGet tool packages.

## Decision Outcome

Chosen option: "Publish platform npm packages and RID-specific NuGet tool
packages", because npm resolves constrained optional dependencies and the .NET
SDK resolves the matching self-contained tool package for each supported RID.

### Consequences

- Good, because .NET tool installation downloads only the matching RID package.
- Good, because npm avoids installing unused platform payloads.
- Bad, because the NuGet RID packages must publish before the pointer package.

### Confirmation

The release workflow builds `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`,
and `osx-arm64` on matching native runners. It validates five archives, creates
each Native AOT RID-specific NuGet package on the same native runner, and
validates the complete artifact set before release. It creates the GitHub
Release first, then publishes five
`@lunarisdigitalsolutions/lunapack-*` npm packages, the
`@lunarisdigitalsolutions/lunapack` entry package, and NuGet RID packages before
the centrally generated NuGet pointer package. Package tests verify target
selection and publication order.

## More Information

The OCI image remains a separate Linux x64 distribution built from the
validated Linux x64 binary.
