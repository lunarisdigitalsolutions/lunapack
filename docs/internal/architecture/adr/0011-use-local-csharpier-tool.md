---
status: accepted
date: 2026-08-10
decision-makers:
  - Florian Bader
---

# ADR-0011: Use Local CSharpier Tool

## Context and Problem Statement

The pre-commit workflow formats staged C# files. It previously used a custom
Node.js wrapper to invoke CSharpier through MSBuild. Maintainers need a direct,
repository-pinned formatter command that works after cloning without a global
tool installation.

## Decision Drivers

- Keep CSharpier versions consistent across contributors.
- Remove custom wrapper code from the formatting workflow.
- Retain automatic cleanup of unnecessary using directives.

## Considered Options

- Use a repository-local CSharpier .NET tool.
- Keep the custom Node.js wrapper around MSBuild.
- Require a globally installed CSharpier tool.

## Decision Outcome

Chosen option: "Use a repository-local CSharpier .NET tool", because the tool
manifest pins the formatter version and exposes `dotnet csharpier format`
directly to the pre-commit hook.

### Consequences

- Good, because contributors use one version after `dotnet tool restore`.
- Good, because staged formatting no longer depends on custom Node.js code.
- Good, because `dotnet format` continues to remove IDE0005 unnecessary using
  directives for staged files.
- Bad, because contributors must restore local .NET tools before the C# hook
  can run.

### Confirmation

Run `dotnet tool restore`, `npm run format:csharp`, and `npx lint-staged` from
the repository root. Verify `dotnet format projects/cli/src/Lunapack.slnx style
--diagnostics IDE0005 --severity info --verify-no-changes` succeeds.

## Pros and Cons of the Options

### Use a Repository-Local CSharpier .NET Tool

- Good, because the tool manifest pins the formatter version in the repository.
- Good, because `dotnet csharpier` is a direct, familiar .NET command.
- Bad, because the local tool must be restored after cloning.

### Keep the Custom Node.js Wrapper Around MSBuild

- Good, because it can orchestrate multiple .NET commands.
- Bad, because C# formatting depends on custom JavaScript maintenance.

### Require a Globally Installed CSharpier Tool

- Good, because it removes the restore step.
- Bad, because developers can run different formatter versions.

## More Information

The CSharpier MSBuild package remains part of the build-time quality baseline.
The local tool serves the pre-commit workflow and developer formatting command.
