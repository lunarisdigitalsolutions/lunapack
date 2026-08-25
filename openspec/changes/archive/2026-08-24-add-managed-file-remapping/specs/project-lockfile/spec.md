## ADDED Requirements

### Requirement: Persist declared and effective managed-file identities

The versioned `lunapack-lock.yml` schema SHALL record each managed file's
manifest-declared target identity, effective project-relative target, installed
SHA-256 digest, and existing strategy data. Lifecycle operations SHALL use the
declared identity to correlate files across release changes and the effective
target to locate files in the project. The lock schema SHALL evolve
compatibly: LunaPack SHALL accept existing lock files, derive declared targets
from their recorded destination behavior when possible, and write the current
schema on the next successful lifecycle mutation. If a declared target cannot
be derived safely, LunaPack SHALL fail without changing project files or state.

#### Scenario: Lock a remapped managed file

- **WHEN** installation remaps `docs/adr/template.md` to
  `docs/architecture/adr/_template.md`
- **THEN** the lock file records the declared target, effective target, and
  installed digest for that managed file

#### Scenario: Upgrade existing destination lock state

- **WHEN** an existing lock file records a directly requested pack destination
  and a lifecycle command successfully mutates its state
- **THEN** the resulting lock file uses the current schema and preserves the
  derived declared and effective targets

#### Scenario: Reject an unresolvable legacy lock record

- **WHEN** LunaPack cannot safely derive a declared target for a legacy managed
  file record
- **THEN** it returns a non-success result without changing project files,
  configuration, or lock state
