# Spec Delta

## MODIFIED Requirements

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
owned by the resolved graph only when their current digest matches the previous
lock-file digest. Strategy selection SHALL apply to retained targets even when
their current hash differs from the hash recorded in the prior lock file.
LunaPack SHALL persist hashes of the resulting target content after a successful
update and SHALL restore all changed targets and state if the update cannot
complete.

When an obsolete target differs from its previous lock-file digest, LunaPack
SHALL preserve the target by default, warn that it was retained, and omit its
stale ownership from the next lock file. `luna update --remove-modified` SHALL
explicitly allow deletion of modified obsolete targets and SHALL affect no
target still owned by the new resolved graph. The dry-run SHALL identify both
preserved modified targets and modified targets selected for deletion by the
override.

Before deleting an obsolete target, LunaPack SHALL verify that its current
content still matches the content observed during planning. If the target
changes after planning, LunaPack SHALL fail the update and roll back the
operation without deleting that changed content.

When multiple prior merge owners share an effective target, LunaPack SHALL
treat that path as one removal unit. A target still planned by any owner SHALL
not be removed. Otherwise, LunaPack SHALL remove it at most once and only when
its content matches the lock-file digest recorded by every prior owner.

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
- **THEN** LunaPack replaces the content between and including those markers
  with the source section

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
  prior resolved graph and the target matches its locked digest
- **THEN** LunaPack removes that target and its lock-file record

#### Scenario: Preserve a locally modified obsolete target

- **WHEN** an updated resolved graph no longer owns a target and its current
  digest differs from the prior lock-file digest
- **THEN** LunaPack retains the file, warns, and removes its stale lock
  ownership while completing the update

#### Scenario: Delete a modified obsolete target with the override

- **WHEN** a user runs `luna update --remove-modified` and an obsolete target
  differs from its prior lock-file digest
- **THEN** LunaPack deletes the target and removes its lock-file record

#### Scenario: Remove stale ownership when an obsolete target is already missing

- **WHEN** an updated resolved graph no longer owns a target and the target is
  already absent
- **THEN** LunaPack completes the update and removes the stale lock-file record

#### Scenario: Preview obsolete-target preservation

- **WHEN** a user dry-runs an update that encounters a modified obsolete target
- **THEN** LunaPack reports that the target will be preserved without modifying
  project files or state

#### Scenario: Preview modified-target deletion with the override

- **WHEN** a user dry-runs `luna update --remove-modified` and an obsolete
  target differs from its prior lock-file digest
- **THEN** LunaPack reports that the target will be deleted without modifying
  project files or state

#### Scenario: Keep a target shared with a remaining owner

- **WHEN** an update removes one prior merge owner but another planned owner
  still uses the same effective target
- **THEN** LunaPack retains the target and records the remaining ownership

#### Scenario: Preserve a modified target shared by merge owners

- **WHEN** an update removes every prior merge owner of a shared target and its
  current digest does not match every prior owner's locked digest
- **THEN** LunaPack preserves the target once, warns once, and removes stale
  ownership

#### Scenario: Revalidate an obsolete target before deletion

- **WHEN** an obsolete target changes after planning but before transaction
  deletion
- **THEN** LunaPack fails the update, preserves the changed target, and rolls
  back other managed-file and state changes
