# Ownership and safety

LunaPack records a SHA-256 digest of each rendered managed file. Installation
creates absent targets and can adopt an existing target only when it exactly
matches the rendered content. A conflicting unowned target stops the operation.

Only merge strategies may share a target. The merged result is deterministic,
and every owner records its final digest. Other target collisions fail before
project state changes.

Uninstall removes an unchanged copy target when it is unreachable. For a
section merge, it removes only that pack's recorded marker-bounded section;
line and JSON merge targets remain unchanged. A missing managed target emits a
warning but does not block ownership-state cleanup. A modified target still
stops uninstall, and a failed installation, update, or state save rolls back
planned file changes.

Consumers select trusted sources. Local paths and Git provenance are recorded
in the lock document.
