# Path Handling

Use this reference when adding or changing CLI, configuration, lock-file, pack
manifest, or filesystem path behavior. It implements the portable-path decision
in [ADR-0037](../architecture/adr/0037-canonicalize-persisted-project-paths.md).

## Canonical Form

LunaPack accepts `/` and `\` in supported path input. Configuration,
`lunapack-lock.yml`, and pack-derived paths persisted by LunaPack use `/` on
every operating system. Path identity comparisons use this slash-only form and
use ordinal comparison.

Do not add ad hoc separator replacement. `ProjectPath` in the CLI project owns
the conversion contract.

## Select The Operation

Use `ProjectPath.Normalize(path)` for a path value that is already constrained
by its owning format or boundary, including pack manifest paths, lock-record
paths, link base paths, link selectors, link mapping prefixes, configured Git
repository paths, glob patterns, and serialization normalization. It changes
only separator representation.

Use `ProjectPath.NormalizeProjectRelativePath(fileSystem, projectDirectory,
path)` for external input that names a project filesystem location, including
CLI path arguments, installation destinations, remapping keys and values, and
managed-file move paths. It:

- accepts either separator;
- rejects empty and rooted paths, including UNC and drive-qualified paths;
- resolves `.` and `..` through the injected filesystem abstraction;
- rejects a location outside the project directory; and
- returns a slash-only, project-relative path without a trailing slash.

Keep URLs, opaque identifiers, and non-path values out of these operations.
Git refs in link definitions and lock evidence are opaque values and must not be
path-normalized.

## Persistence And Boundaries

`ProjectStateStore` normalizes state after deserialization and before
serialization. New configuration or lock-file fields that contain paths must be
included in that normalization. Pack discovery normalizes manifest path fields
before validation and planning. Command handlers normalize user-supplied
project paths before ownership, collision, or filesystem checks.

Link resolution normalizes base paths, include and exclude selectors, strip
prefixes, declared targets, selected source paths, and effective lock targets.
Selection containment is checked against the source snapshot; mapped target
containment is checked against the workspace. Local enumeration rejects
symlinks and reparse points before reading bytes.

Pack authoring applies `NormalizeProjectRelativePath` to file, directory,
target, and lifecycle-script file input. Glob input uses `Normalize` before
separate checks reject rooted and parent-traversing patterns. Persisted
`pack.yml` paths always use `/`.

External selectors first pass through the managed-file compatibility adapter:
legacy `source`-only files remain pack-relative, while `source` plus `path`,
`directory`, or `glob` identifies a pack-local alias. Normalize selected paths
and exclusions before resolving them below the materialized source root. Reject
rooted paths, `..` escapes, links outside the root, empty matches, and duplicate
flattened target names before project target planning.

When a caller needs additional semantics such as trimming a repository-relative
Git path, apply that operation after `ProjectPath` normalization. Do not create
a second separator-normalization implementation.

## Verification

Add focused tests for each new path boundary. Cover Windows-style input,
slash-only persisted output, `.` and `..` behavior where project-relative input
is accepted, and matching against an existing canonical lock target. Run the
relevant lifecycle or process test when changing path identity or persistence.
