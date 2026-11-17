# Spec Delta

## MODIFIED Requirements

### Requirement: Select a cataloged version for installation

LunaPack SHALL accept one or more `luna install <pack-id>` or `luna install
<pack-id>@<version>` references. It SHALL resolve directly requested root
candidates from the configured source catalog, considering only releases whose
`directInstallable` property is true or omitted. Without an explicit version,
LunaPack SHALL select the highest eligible semantic version according to
Semantic Versioning precedence. With an explicit version, LunaPack SHALL select
that available version only when it is directly installable. When eligible
candidates have equal version precedence, LunaPack SHALL select the candidate
from the earliest configured source. LunaPack SHALL record an explicit root
version request in `lunapack.yml` when one was supplied and SHALL record all
selected exact versions in `lunapack-lock.yml`. `luna install` SHALL also accept
`--dry-run` and report its resolved version and planned target actions without
mutation.

#### Scenario: Install an explicit version

- **WHEN** a configured local source catalogs multiple versions of a pack and
  a user runs `luna install <pack-id>@<version>` for an available,
  directly-installable version
- **THEN** LunaPack installs that version, records the root request in
  `lunapack.yml`, and records the selected version in `lunapack-lock.yml`

#### Scenario: Install the latest available version

- **WHEN** a configured local source catalogs multiple versions of a pack and
  a user runs `luna install <pack-id>` without a version
- **THEN** LunaPack installs the highest directly-installable version and
  records that selection in `lunapack-lock.yml`

#### Scenario: Reject an unavailable requested version

- **WHEN** a user requests a version absent from the source catalog or marked
  `directInstallable: false`
- **THEN** LunaPack returns a non-success result, suggests the latest eligible
  version when available, and does not change project files, configuration, or
  lock state

#### Scenario: Skip an installed root in a multi-reference installation

- **WHEN** a user supplies an already installed requested root and another root
  to one `luna install` invocation
- **THEN** LunaPack warns that the installed root was skipped and continues
  processing the remaining references

#### Scenario: Prefer the earliest configured source for equal versions

- **WHEN** multiple configured sources provide the same directly-installable
  package ID and version
- **THEN** LunaPack installs the candidate from the earliest configured source

#### Scenario: Preview a selected installation version

- **WHEN** a user runs `luna install <pack-id> --dry-run`
- **THEN** LunaPack reports the selected latest eligible version and planned
  target actions without changing project files, configuration, or lock state

### Requirement: Update installed root packs

LunaPack SHALL accept `luna update <pack-id>@<version>` to update an installed
requested root to an available, directly-installable explicit semantic
version, and `luna update <pack-id>` to update that root to the highest
directly-installable semantic version from the configured sources. `luna update`
without a pack reference SHALL update every installed requested root with a
newer eligible version or changed selected external-source content. Root
candidate selection SHALL use existing semantic-version precedence and
configured-source precedence rules, and SHALL not select a root release marked
`directInstallable: false`.

An update SHALL resolve the complete requested-root graph before mutation,
including changed external source requirements and current commits for symbolic
refs. It SHALL apply added, changed, and removed managed targets, including
changed glob membership, then persist the selected root request and complete
resolved lock graph together. A pack MAY update its externally sourced files
while its version remains unchanged. Removed source requirements SHALL be
removed from lock consumers but their workspace source entries SHALL remain
configured without cleanup prompts or suggestions. The command SHALL report a
non-success result without mutation when the requested root is not installed,
the requested explicit version is unavailable or not directly installable,
source approval or graph resolution fails, configuration drift is unaccepted,
preflight fails, or a target strategy cannot be applied. A pack whose version
and selected source content are current SHALL remain unchanged and be reported
as current.

#### Scenario: Update a named pack to its latest version

- **WHEN** a user runs `luna update <pack-id>` and a newer
  directly-installable release exists
- **THEN** LunaPack applies the selected release's managed-file strategies and
  records the newest eligible version and target hashes

#### Scenario: Update a named pack to an explicit available version

- **WHEN** a user runs `luna update <pack-id>@<version>` for an installed root
  and that version is available and directly installable
- **THEN** LunaPack installs that exact selected version and persists it as the
  root request

#### Scenario: Reject a dependency-only update version

- **WHEN** a user explicitly updates an installed root to a release marked
  `directInstallable: false`
- **THEN** LunaPack returns a non-success result without changing project files
  or state

#### Scenario: Update external content without a pack version change

- **WHEN** a symbolic external source ref resolves to a new commit and one or
  more selected files or glob members changed
- **THEN** LunaPack plans and applies those managed-file additions, changes,
  and removals while retaining the pack version

#### Scenario: Retain an unused configured source after update

- **WHEN** the updated graph no longer requires a previously consumed
  workspace source
- **THEN** LunaPack removes its lock consumers and keeps the source in
  `lunapack.yml` without cleanup guidance

#### Scenario: Reject an update for an uninstalled root

- **WHEN** a user runs `luna update unknown-pack`
- **THEN** LunaPack returns a non-success result without changing project files
  or state

#### Scenario: Update all available installed roots

- **WHEN** a user runs `luna update` and multiple installed requested roots
  have newer eligible versions or changed selected external content
- **THEN** LunaPack updates each eligible root and reports its selected version
  and source-content changes

### Requirement: Report outdated installed packs

LunaPack SHALL accept `luna outdated` and list every installed requested root
whose highest directly-installable configured-source version has greater
semantic-version precedence or whose selected external-source files differ
from locked content. It SHALL inspect dependency versions, current external
symbolic refs, selected-file content, added or removed glob matches, missing
configured fingerprints, and source configuration drift. A moved external ref
SHALL not make a pack outdated when the selected file set and content hashes
are unchanged. `--offline` SHALL use available cache and lock evidence, SHALL
not contact remotes, and SHALL state that remote refs were not checked. Each
result SHALL include the pack ID, current version, available version, and
reason. When no requested root is outdated, the command SHALL report that no
updates are available and leave the project unchanged.

#### Scenario: List available updates

- **WHEN** an installed root is at `1.0.0` and configured sources contain a
  newer directly-installable version `1.1.0`
- **THEN** `luna outdated` reports that root with current version `1.0.0`,
  latest version `1.1.0`, and reason `pack update`

#### Scenario: Report changed external content

- **WHEN** a pack version is unchanged but a selected external file differs at
  the current resolved ref
- **THEN** `luna outdated` reports that pack with reason `external source
changed`

#### Scenario: Ignore an irrelevant ref movement

- **WHEN** an external ref resolves to a new commit but every selected file and
  glob membership remains unchanged
- **THEN** `luna outdated` does not classify the pack as outdated for that
  source movement

#### Scenario: Check outdated state offline

- **WHEN** a user runs `luna outdated --offline`
- **THEN** LunaPack uses cache and lock information and reports that remote refs
  were not checked

## ADDED Requirements

### Requirement: Resolve dependency packs regardless of direct-installability

Composite dependency resolution SHALL consider cataloged releases regardless
of `directInstallable`. The property SHALL restrict only selection as a
directly requested root; it SHALL not block a release referenced by another
pack.

#### Scenario: Resolve a dependency-only composite reference

- **WHEN** a directly-installable composite pack references an available
  release marked `directInstallable: false`
- **THEN** LunaPack resolves and installs that release as a dependency without
  treating it as a requested root
