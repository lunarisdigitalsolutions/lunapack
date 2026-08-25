# Architecture overview

Luna is a .NET 10 Native AOT command-line application. System.CommandLine owns
parsing and help; command handlers resolve the workspace and delegate catalog,
project-state, trust, or lifecycle work to focused services.

`lunapack.yml` is portable desired state. `lunapack-lock.yml` is generated
resolved state containing exact versions, source provenance, dependency edges,
declared and effective targets, ownership, and installed digests. The state
store validates and writes both documents as one recoverable change.

Catalog adapters discover local and Git sources. Resolution builds an exact
dependency graph before mutation. Installation and update planners bind typed
parameters, evaluate conditions, render opt-in Scriban templates, authorize
hooks, and preflight managed-file actions. Transactions snapshot changed files
and restore them when an action or state save fails.

Filesystem access uses System.IO.Abstractions at testable boundaries. Native AOT
constraints require static YAML serialization and prohibit runtime-dependent
reflection. Unit tests cover services and handlers; integration tests execute
the built CLI in isolated temporary repositories.
