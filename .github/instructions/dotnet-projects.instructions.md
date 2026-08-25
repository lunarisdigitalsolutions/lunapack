---
applyTo: '**/*.{csproj,props,targets,slnx}'
---

# .NET Project Configuration

Read the [C# coding guidelines](../../docs/internal/development/coding-guidelines/csharp.md)
before changing solution, project, or MSBuild files. Keep shared compiler,
analyzer, formatting, and test-package policy in `Directory.Build.props`.
Manage package versions centrally in `Directory.Packages.props`, preserve lock
file behavior, and avoid per-project versions unless the central policy changes.
