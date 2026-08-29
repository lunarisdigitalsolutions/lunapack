# Ownership and safety

LunaPack records a SHA-256 digest of each rendered managed file. Installation
creates absent targets and can adopt an existing target only when it exactly
matches the rendered content. A conflicting unowned target stops the operation.

Only merge strategies may share a target. The merged result is deterministic,
and every owner records its final digest. Other target collisions fail before
project state changes.

Update and uninstall treat local changes differently. Update leaves local drift
alone when newly rendered pack content still matches the previous locked
digest. If a newer pack stops declaring a target, however, update deletes that
obsolete target without a drift check. Preview updates before applying them;
use an `@ignore` remapping when an obsolete managed file must remain locally.

Uninstall removes an unchanged copy target when it is unreachable. For a
section merge, it removes only that pack's recorded marker-bounded section;
line and JSON merge targets remain unchanged. A missing managed target emits a
warning but does not block ownership-state cleanup. A modified target still
stops uninstall, and a failed installation, update, or state save rolls back
planned file changes.

Reachability comes from exact dependency edges already stored in the lock file.
After removing a root, Luna walks every remaining root's locked dependencies. A
shared dependency and its files remain while any root can still reach it, then
become removable with the last root. Fresh source discovery is not required for
this ownership decision; Luna accesses source content separately when it can
load uninstall hooks.

Consumers select trusted sources. Local paths and Git provenance are recorded
in the lock document.
