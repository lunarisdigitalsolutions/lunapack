## ADDED Requirements

### Requirement: Update installed root packs

LunaPack SHALL accept `lunapack update <pack-id>@<version>` to update an installed
requested root to an available explicit semantic version, and `lunapack update
<pack-id>` to update that root to the highest available semantic version from
the configured sources. `lunapack update` without a pack reference SHALL update
every installed requested root that has a newer available version. Candidate
selection SHALL use existing semantic-version precedence and configured-source
precedence rules.

An update SHALL resolve the complete requested-root graph before mutation,
apply added, changed, and removed managed targets, then persist the selected
root request and complete resolved lock graph together. The command SHALL
report a non-success result without mutation when the requested root is not
installed, the requested explicit version is unavailable, graph resolution or
preflight fails, or a target strategy cannot be applied. A pack that already
uses the selected version SHALL remain unchanged and be reported as current.

#### Scenario: Update a named pack to its latest version

- **WHEN** a user runs `lunapack update dotnet-sdk-10` and a newer release exists
- **THEN** LunaPack applies the selected release's managed-file strategies and
  records the newest resolved version and target hashes

#### Scenario: Update a named pack to an explicit available version

- **WHEN** a user runs `lunapack update dotnet-sdk-10@1.2.0` for an installed root
  and that version is available
- **THEN** LunaPack installs that exact selected version and persists it as the
  root request

#### Scenario: Reject an update for an uninstalled root

- **WHEN** a user runs `lunapack update unknown-pack`
- **THEN** LunaPack returns a non-success result without changing project files or
  state

#### Scenario: Update all available installed roots

- **WHEN** a user runs `lunapack update` and multiple installed requested roots
  have newer available versions
- **THEN** LunaPack updates each eligible root and reports its selected newest
  version

### Requirement: Report outdated installed packs

LunaPack SHALL accept `lunapack outdated` and list every installed requested root
whose highest available configured-source version has greater semantic-version
precedence than its currently resolved version. Each result SHALL include the
pack ID, current version, and latest available version. When no requested root
is outdated, the command SHALL report that no updates are available and leave
the project unchanged.

#### Scenario: List available updates

- **WHEN** an installed root is at `1.0.0` and configured sources contain
  `1.1.0`
- **THEN** `lunapack outdated` reports that root with current version `1.0.0` and
  latest version `1.1.0`

### Requirement: Preview and confirm package changes

LunaPack SHALL accept `--dry-run` on `lunapack install` and every form of `lunapack
update`. A dry run SHALL perform source resolution and preflight, report each
planned target action and selected version, and SHALL not write, delete,
rename, or otherwise modify project files, `lunapack.yml`, or `lunapack-lock.yml`.

LunaPack SHALL accept `--prompt` on `lunapack update` without a pack reference. It
SHALL show each eligible pack and newest version, request confirmation before
that pack's update, update only confirmed packs, and leave declined packs
unchanged.

#### Scenario: Preview an install

- **WHEN** a user runs `lunapack install dotnet-sdk-10 --dry-run`
- **THEN** LunaPack reports the planned selected release and file actions without
  modifying files or state

#### Scenario: Preview an update

- **WHEN** a user runs `lunapack update dotnet-sdk-10 --dry-run`
- **THEN** LunaPack reports additions, removals, and strategy-driven changes
  without modifying files or state

#### Scenario: Confirm updates individually

- **WHEN** a user runs `lunapack update --prompt` and declines one of two
  available updates
- **THEN** LunaPack updates the confirmed pack and leaves the declined pack's
  files and state unchanged

### Requirement: Apply managed-file update strategies

Each `managedFiles` entry in `pack.yml` SHALL support an optional `strategy`
with a `type` and `method`. An omitted strategy SHALL behave as `copy` with
`overwrite` for compatibility with existing manifests. `copy` SHALL accept
`overwrite`, `fail-if-exists`, `skip-if-exists`, and `backup-and-overwrite`.
`merge` SHALL accept `lines`, `section`, and `json`. Invalid strategy
combinations SHALL make a manifest invalid before project mutation.

During an update, LunaPack SHALL compare the prior resolved target set with the
new resolved target set. It SHALL add new targets, apply the selected strategy
to retained targets whose source content changes, and remove targets no longer
owned by the resolved graph. Strategy selection SHALL apply even when the
current target hash differs from the hash recorded in the prior lock file.
LunaPack SHALL persist hashes of the resulting target content after a successful
update and SHALL restore all changed targets and state if the update cannot
complete.

For `copy`, `overwrite` SHALL replace a target; `fail-if-exists` SHALL fail if
the target exists; `skip-if-exists` SHALL retain an existing target; and
`backup-and-overwrite` SHALL rename the existing target with a unique numeric
suffix before replacement. For `merge`, `lines` SHALL append source lines not
already present in the target; `section` SHALL use the source first and last
lines as markers, append the source when markers are absent, or replace the
marked destination section when present; and `json` SHALL merge a source JSON
object or array into a same-kind target without removing target entries. For
arrays, it SHALL retain destination order and append only source elements that
are not structurally equal to an existing destination element.

#### Scenario: Update a copy-overwrite target

- **WHEN** an updated pack declares `copy/overwrite` for an existing target
- **THEN** LunaPack replaces that target with the updated source content and
  records its resulting hash

#### Scenario: Merge new ignore lines

- **WHEN** an updated pack declares `merge/lines` and the target already
  contains some source lines
- **THEN** LunaPack appends only source lines absent from the target

#### Scenario: Replace a marked section

- **WHEN** an updated pack declares `merge/section` and the destination
  contains the source's first and last lines as markers
- **THEN** LunaPack replaces the content between and including those markers with
  the source section

#### Scenario: Merge JSON values without removal

- **WHEN** an updated pack declares `merge/json` for matching JSON object
  targets
- **THEN** LunaPack adds source properties and overwrites colliding property
  values without removing destination properties

#### Scenario: Merge JSON array values without duplicates

- **WHEN** an updated pack declares `merge/json` for matching JSON array
  targets with overlapping entries
- **THEN** LunaPack preserves destination order and appends only source entries
  that are not structurally equal to destination entries

#### Scenario: Remove a target absent from the new release

- **WHEN** an updated resolved graph no longer owns a target recorded by the
  prior resolved graph
- **THEN** LunaPack removes that target and removes its lock-file record

## MODIFIED Requirements

### Requirement: Select a cataloged version for installation

LunaPack SHALL accept both `lunapack install <pack-id>` and `lunapack install
<pack-id>@<version>`. It SHALL resolve root pack candidates from the configured
source catalog. Without an explicit version, LunaPack SHALL select the highest
available semantic version according to Semantic Versioning precedence. With
an explicit version, LunaPack SHALL select that available version. When candidates
have equal version precedence, LunaPack SHALL select the candidate from the
earliest configured source. LunaPack SHALL record an explicit root version request
in `lunapack.yml` when one was supplied and SHALL record all selected exact
versions in `lunapack-lock.yml`. `lunapack install` SHALL also accept `--dry-run` and
report its resolved version and planned target actions without mutation.

#### Scenario: Install an explicit version

- **WHEN** a configured local source catalogs multiple versions of a pack and a user runs `lunapack install <pack-id>@<version>` for one available version
- **THEN** LunaPack installs that version, records the root request in `lunapack.yml`, and records the selected version in `lunapack-lock.yml`

#### Scenario: Install the latest available version

- **WHEN** a configured local source catalogs multiple versions of a pack and a user runs `lunapack install <pack-id>` without a version
- **THEN** LunaPack installs the highest available version and records that selection in `lunapack-lock.yml`

#### Scenario: Reject an unavailable requested version

- **WHEN** a user requests a package version that is absent from the configured source catalog
- **THEN** LunaPack returns a non-success result and does not change project files, configuration, or lock state

#### Scenario: Prefer the earliest configured source for equal versions

- **WHEN** multiple configured sources provide the same package ID and version
- **THEN** LunaPack installs the candidate from the earliest configured source

#### Scenario: Preview a selected installation version

- **WHEN** a user runs `lunapack install <pack-id> --dry-run`
- **THEN** LunaPack reports the selected latest version and planned target actions
  without changing project files, configuration, or lock state
