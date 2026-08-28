# Local Pack Lifecycle Delta Specification

## MODIFIED Requirements

### Requirement: Resolve composite pack references from configured sources

LunaPack SHALL recursively resolve every composite pack reference from the local and Git sources configured in the consuming project's `lunapack.yml`. Each composite reference SHALL resolve the declared ID and exact version using the same source-precedence rules as direct installation. After resolving the complete pack graph, LunaPack SHALL read each selected pack's external Git source declarations only to resolve managed content declared by that pack; pack-local aliases SHALL not be inherited or used for pack discovery.

#### Scenario: Install a composite pack from configured sources

- **WHEN** a user installs a composite pack whose referenced packs are present in configured sources
- **THEN** LunaPack resolves and installs the composite pack, all references, their used external-source requirements, and their managed files

#### Scenario: Resolve a composite reference from the earliest configured source

- **WHEN** equal ID-and-version composite candidates exist in multiple configured sources
- **THEN** LunaPack selects the candidate from the earliest configured source

#### Scenario: Resolve a Git-sourced composite reference

- **WHEN** a Git-sourced composite pack references an exact pack version available from configured Git or local sources
- **THEN** LunaPack resolves that reference using the same configured-source precedence as a direct installation

#### Scenario: Keep pack aliases scoped

- **WHEN** two packs in one graph use the same alias for different external-source fingerprints
- **THEN** LunaPack resolves each alias only within its declaring pack

#### Scenario: Refuse a missing composite reference

- **WHEN** a composite pack references an ID and version absent from configured sources
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

### Requirement: Install and update Git-sourced packs transactionally

LunaPack SHALL resolve every pack source and used external source, select immutable commits, resolve all managed-file selections, and validate the complete operation before mutating managed files, `lunapack.yml`, or `lunapack-lock.yml`. Installation and update SHALL commit approved source additions, managed-file changes, and lock state as one transaction. A rejection, cancellation, Git failure, empty required selection, unsafe path, target conflict, or state-write failure SHALL leave configuration, managed files, and lock state unchanged. When direct atomic replacement is unavailable, LunaPack SHALL use backups and best-effort rollback that never intentionally leaves managed files without ownership records.

#### Scenario: Refuse a failed Git-source installation

- **WHEN** a selected pack or required external source cannot be materialized at its resolved commit
- **THEN** LunaPack returns a non-success result without changing managed files, `lunapack.yml`, or `lunapack-lock.yml`

#### Scenario: Update a Git-sourced root pack

- **WHEN** a user updates an installed root pack and a higher version is available from its configured Git source
- **THEN** LunaPack applies the selected version and persists its pack and external-source resolution evidence with the updated lock state

#### Scenario: Roll back an approved source after a target conflict

- **WHEN** an install approves a missing external source but later preflight detects a managed-target conflict
- **THEN** LunaPack does not retain the source, files, or proposed lock state

### Requirement: Update installed root packs

LunaPack SHALL accept `luna update <pack-id>@<version>` to update an installed requested root to an available explicit semantic version, and `luna update <pack-id>` to update that root to the highest available semantic version from the configured sources. `luna update` without a pack reference SHALL update every installed requested root with a newer available version or changed selected external-source content. Candidate selection SHALL use existing semantic-version precedence and configured-source precedence rules.

An update SHALL resolve the complete requested-root graph before mutation, including changed external source requirements and current commits for symbolic refs. It SHALL apply added, changed, and removed managed targets, including changed glob membership, then persist the selected root request and complete resolved lock graph together. A pack MAY update its externally sourced files while its version remains unchanged. Removed source requirements SHALL be removed from lock consumers but their workspace source entries SHALL remain configured without cleanup prompts or suggestions. The command SHALL report a non-success result without mutation when the requested root is not installed, the requested explicit version is unavailable, source approval or graph resolution fails, configuration drift is unaccepted, preflight fails, or a target strategy cannot be applied. A pack whose version and selected source content are current SHALL remain unchanged and be reported as current.

#### Scenario: Update a named pack to its latest version

- **WHEN** a user runs `luna update dotnet-sdk-10` and a newer release exists
- **THEN** LunaPack applies the selected release's managed-file strategies and records the newest resolved version and target hashes

#### Scenario: Update a named pack to an explicit available version

- **WHEN** a user runs `luna update dotnet-sdk-10@1.2.0` for an installed root and that version is available
- **THEN** LunaPack installs that exact selected version and persists it as the root request

#### Scenario: Update external content without a pack version change

- **WHEN** a symbolic external source ref resolves to a new commit and one or more selected files or glob members changed
- **THEN** LunaPack plans and applies those managed-file additions, changes, and removals while retaining the pack version

#### Scenario: Retain an unused configured source after update

- **WHEN** the updated graph no longer requires a previously consumed workspace source
- **THEN** LunaPack removes its lock consumers and keeps the source in `lunapack.yml` without cleanup guidance

#### Scenario: Reject an update for an uninstalled root

- **WHEN** a user runs `luna update unknown-pack`
- **THEN** LunaPack returns a non-success result without changing project files or state

#### Scenario: Update all available installed roots

- **WHEN** a user runs `luna update` and multiple installed requested roots have newer versions or changed selected external content
- **THEN** LunaPack updates each eligible root and reports its selected version and source-content changes

### Requirement: Report outdated installed packs

LunaPack SHALL accept `luna outdated` and list every installed requested root whose highest available configured-source version has greater semantic-version precedence or whose selected external-source files differ from locked content. It SHALL inspect dependency versions, current external symbolic refs, selected-file content, added or removed glob matches, missing configured fingerprints, and source configuration drift. A moved external ref SHALL not make a pack outdated when the selected file set and content hashes are unchanged. `--offline` SHALL use available cache and lock evidence, SHALL not contact remotes, and SHALL state that remote refs were not checked. Each result SHALL include the pack ID, current version, available version, and reason. When no requested root is outdated, the command SHALL report that no updates are available and leave the project unchanged.

#### Scenario: List available updates

- **WHEN** an installed root is at `1.0.0` and configured sources contain `1.1.0`
- **THEN** `luna outdated` reports that root with current version `1.0.0`, latest version `1.1.0`, and reason `pack update`

#### Scenario: Report changed external content

- **WHEN** a pack version is unchanged but a selected external file differs at the current resolved ref
- **THEN** `luna outdated` reports that pack with reason `external source changed`

#### Scenario: Ignore an irrelevant ref movement

- **WHEN** an external ref resolves to a new commit but every selected file and glob membership remains unchanged
- **THEN** `luna outdated` does not classify the pack as outdated for that source movement

#### Scenario: Check outdated state offline

- **WHEN** a user runs `luna outdated --offline`
- **THEN** LunaPack uses cache and lock information and reports that remote refs were not checked

### Requirement: Preview and confirm package changes

LunaPack SHALL accept `--dry-run` on `luna install` and every form of `luna update`. A dry run SHALL perform dependency, source mapping, source resolution, selection, and target preflight; report reused source mappings, proposed source additions, whether approval would be required, selected pack versions, and each planned target action; and SHALL not prompt for final approval or modify project files, `lunapack.yml`, or `lunapack-lock.yml`.

LunaPack SHALL accept `--prompt` on `luna update` without a pack reference. It SHALL show each eligible pack and newest version or external-content reason, request confirmation before that pack's update, update only confirmed packs, and leave declined packs unchanged.

#### Scenario: Preview an install

- **WHEN** a user runs `luna install dotnet-sdk-10 --dry-run` for a graph requiring a missing external source
- **THEN** LunaPack reports the proposed source addition and file actions without prompting or modifying files or state

#### Scenario: Preview an update

- **WHEN** a user runs `luna update dotnet-sdk-10 --dry-run`
- **THEN** LunaPack reports source mappings, source additions, version changes, and file additions, removals, and strategy-driven changes without modifying files or state

#### Scenario: Confirm updates individually

- **WHEN** a user runs `luna update --prompt` and declines one of two available updates
- **THEN** LunaPack updates the confirmed pack and leaves the declined pack's files and state unchanged

## ADDED Requirements

### Requirement: Resolve and approve graph-wide external source requirements

Before installation or update mutation, LunaPack SHALL collect only external source declarations referenced by managed files in the complete resolved pack graph, canonicalize them, group equivalent requirements by fingerprint, and match each group against workspace sources. An existing fingerprint match SHALL be reused under its authoritative workspace identifier without approval even when pack aliases differ. Each missing fingerprint SHALL propose one identifier from its pack aliases. If that identifier is occupied by another fingerprint, an interactive command SHALL continue requesting a valid unused identifier or permit cancellation. LunaPack SHALL present all conflict-free missing sources in one sanitized approval prompt with repository identity, canonical ref, base path, description when present, requiring packs and aliases, and file-entry count. Approval SHALL default to no and SHALL be all or nothing.

#### Scenario: Reuse an existing workspace source

- **WHEN** a pack alias normalizes to the fingerprint of a configured workspace source under a different identifier
- **THEN** LunaPack maps the alias to the existing identifier without adding a source or requesting approval

#### Scenario: Deduplicate requirements across dependencies

- **WHEN** multiple packs declare equivalent source fingerprints under different aliases
- **THEN** LunaPack presents at most one source addition and records each pack alias mapping to the same workspace identifier

#### Scenario: Ignore an unused source declaration

- **WHEN** a selected pack declares an external source that no managed file references
- **THEN** LunaPack does not resolve, approve, configure, or lock that declaration

#### Scenario: Reject combined source approval

- **WHEN** a user declines the combined missing-source prompt
- **THEN** LunaPack returns a non-success result without changing configuration, managed files, or lock state

#### Scenario: Resolve an identifier conflict interactively

- **WHEN** a proposed source identifier belongs to a different configured fingerprint and interaction is available
- **THEN** LunaPack displays sanitized existing and required identities and continues prompting until the user supplies a valid unused identifier or cancels

#### Scenario: Fail when interaction is unavailable

- **WHEN** a missing source requires approval or identifier conflict resolution and interaction is unavailable
- **THEN** LunaPack returns a non-success result with a complete manual `luna sources add` command and leaves project state unchanged

#### Scenario: Accept conflict-free sources non-interactively

- **WHEN** a user supplies `--accept-sources` and every missing source has a valid available proposed identifier
- **THEN** LunaPack approves those additions without bypassing validation, path safety, script trust, authentication constraints, or transactionality

#### Scenario: Reject a non-interactive identifier conflict

- **WHEN** a user supplies `--accept-sources` but a proposed identifier is occupied by a different fingerprint
- **THEN** LunaPack returns a non-success result and requires the source to be configured explicitly under another identifier

### Requirement: Install external selections under pack ownership

For an approved and resolved external source, LunaPack SHALL expand a declared single file, recursive directory, or glob below the normalized source root, apply exclusions after primary selection, and preserve relative paths below the selection root under the declared target. When flattening is enabled, LunaPack SHALL map each selected file to its basename and reject duplicate target names. Empty required selections and source or target paths that escape their approved roots SHALL fail preflight. Every resulting target SHALL use existing conflict, strategy, remapping, template, local-modification, update, and uninstall rules and SHALL remain owned by the declaring pack rather than the external source.

#### Scenario: Install a recursive external directory

- **WHEN** a pack selects an external directory and the graph's sources are approved and resolved
- **THEN** LunaPack recursively installs its files below the target while recording the declaring pack as owner

#### Scenario: Apply exclusions after glob matching

- **WHEN** an external glob matches files also matched by one or more exclusions
- **THEN** LunaPack removes excluded files from the planned selection before target mapping

#### Scenario: Reject a flattened collision

- **WHEN** flattening maps two selected external files to the same target basename
- **THEN** LunaPack returns a non-success result before changing project state

#### Scenario: Reject a pattern with no files

- **WHEN** a required external file, directory, or glob resolves to no files
- **THEN** LunaPack identifies the selector and source and returns a non-success result without changing project state

### Requirement: Detect external source drift and audit provenance

LunaPack SHALL compare each locked external fingerprint with the current authoritative workspace source before update. A changed repository, canonical ref, or base path SHALL be reported as configuration drift and SHALL block automatic update unless a separate explicit source-identity acceptance workflow authorizes it. `luna audit` SHALL report each external source's owning pack and version, pack alias, workspace identifier, sanitized fingerprint components, canonical requested ref, resolved commit, managed source and target paths, and local modification status. Audit SHALL detect missing workspace sources, duplicate fingerprints, fingerprint mismatches, configuration drift, missing resolved commits, missing source paths, missing or locally modified targets, and content-hash drift.

#### Scenario: Block drifted source configuration

- **WHEN** a configured workspace source no longer matches the fingerprint recorded for an installed pack
- **THEN** update reports locked and configured source identities and returns a non-success result without changing state

#### Scenario: Audit external file provenance

- **WHEN** a user audits a pack with externally sourced files
- **THEN** LunaPack displays pack ownership, alias mapping, workspace source, canonical ref, resolved commit, paths, hashes, and status
