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
paths, configured Git repository paths, glob patterns, and serialization
normalization. It changes only separator representation.

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

## Persistence And Boundaries

`ProjectStateStore` normalizes state after deserialization and before
serialization. New configuration or lock-file fields that contain paths must be
included in that normalization. Pack discovery normalizes manifest path fields
before validation and planning. Command handlers normalize user-supplied
project paths before ownership, collision, or filesystem checks.

When a caller needs additional semantics such as trimming a repository-relative
Git path, apply that operation after `ProjectPath` normalization. Do not create
a second separator-normalization implementation.

## Verification

Add focused tests for each new path boundary. Cover Windows-style input,
slash-only persisted output, `.` and `..` behavior where project-relative input
is accepted, and matching against an existing canonical lock target. Run the
relevant lifecycle or process test when changing path identity or persistence.
