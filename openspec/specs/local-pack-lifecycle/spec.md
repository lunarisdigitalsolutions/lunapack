# local-pack-lifecycle Specification

## Purpose

Define installation and safe removal of versioned packs from configured local LunaPack sources.

## Requirements

### Requirement: Audit installed link ownership

`luna audit` SHALL evaluate every installed link target against its locked effective path and SHA-256 digest and SHALL report missing, locally modified, and ownership-conflicting files. Audit SHALL not mutate project files, configuration, or lock state.

#### Scenario: Report a locally modified linked file

- **WHEN** an installed link target's current digest differs from its locked digest
- **THEN** `luna audit` identifies the link and modified target without changing it

#### Scenario: Report a missing linked file

- **WHEN** an installed link target no longer exists
- **THEN** `luna audit` identifies the link and missing target without recreating it

### Requirement: Uninstall links with digest protection

`luna uninstall <name>` SHALL remove an installed link's definition, unchanged exclusively owned targets, and resolved lock record atomically. If any owned target differs from its recorded digest, LunaPack SHALL preserve every file, definition, and lock state and SHALL return a non-success result. Uninstalling a link SHALL not affect unrelated packs or links.

#### Scenario: Uninstall an unchanged link

- **WHEN** every target owned by an installed link matches its locked digest
- **THEN** LunaPack removes those targets, the link definition, and its lock record atomically

#### Scenario: Preserve a modified link installation

- **WHEN** any target owned by an installed link differs from its locked digest
- **THEN** LunaPack returns a non-success result and preserves all managed files, configuration, and lock state

### Requirement: Resolve composite pack references from configured sources

LunaPack SHALL recursively resolve every composite pack reference from the local and Git sources configured in the consuming project's `lunapack.yml`. Each composite reference SHALL resolve the declared ID and exact version using the same source-precedence rules as direct installation. LunaPack SHALL not read source configuration from a pack manifest.

#### Scenario: Install a composite pack from configured sources

- **WHEN** a user installs a composite pack whose referenced packs are present in configured sources
- **THEN** LunaPack resolves and installs the composite pack, all references, and their managed files

#### Scenario: Resolve a composite reference from the earliest configured source

- **WHEN** equal ID-and-version composite candidates exist in multiple configured sources
- **THEN** LunaPack selects the candidate from the earliest configured source

#### Scenario: Resolve a Git-sourced composite reference

- **WHEN** a Git-sourced composite pack references an exact pack version available from configured Git or local sources
- **THEN** LunaPack resolves that reference using the same configured-source precedence as a direct installation

#### Scenario: Refuse a missing composite reference

- **WHEN** a composite pack references an ID and version absent from configured sources
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

### Requirement: Install and update Git-sourced packs transactionally

LunaPack SHALL use the Git-source resolved commit selected during an install or update to read the complete selected pack content before it mutates managed files, `lunapack.yml`, or `lunapack-lock.yml`. A Git materialization or source-resolution failure SHALL leave project files and state unchanged.

#### Scenario: Refuse a failed Git-source installation

- **WHEN** a selected Git-source pack cannot be materialized at its resolved commit
- **THEN** LunaPack returns a non-success result without changing managed files, `lunapack.yml`, or `lunapack-lock.yml`

#### Scenario: Update a Git-sourced root pack

- **WHEN** a user updates an installed root pack and a higher version is available from its configured Git source
- **THEN** LunaPack applies the selected version and persists its Git resolution evidence with the updated lock state

### Requirement: Reject invalid composite graphs before installation

LunaPack SHALL reject a composite graph that contains a dependency cycle, resolves the same pack ID to conflicting versions, or declares managed targets that conflict with another pack in the graph or existing unowned project files. LunaPack SHALL validate the complete graph before writing managed files or persisting project state.

#### Scenario: Refuse a composite dependency cycle

- **WHEN** composite references form a direct or transitive cycle
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

#### Scenario: Refuse conflicting target ownership

- **WHEN** two packs in a resolved composite graph declare the same target path
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

### Requirement: Discover and install bundled managed-file packs

LunaPack SHALL provide versioned `dotnet-gitignore`, `dotnet-sdk-10`, `dotnet-editorconfig`, `dotnet-csharpier-tool`, `dotnet-quality-baseline`, and `madr-adr-template` packs in the repository's local pack source. Each pack SHALL declare one or more managed files containing complete reusable engineering-convention content.

The `luna install <pack-id>` command SHALL discover each bundled pack from configured local sources, create each declared target when it does not already exist, and record the pack identity, resolved version, source, managed target paths, and installed content digests in `lunapack-lock.yml`.

#### Scenario: Install a single-file bundled pack from a configured local source

- **WHEN** a user initializes an otherwise empty project, adds the repository local pack source, and runs `luna install dotnet-editorconfig`
- **THEN** the project contains the pack's `.editorconfig` content and `lunapack.yml` records the installed pack and managed file

#### Scenario: Install the multi-file quality baseline

- **WHEN** a user installs `dotnet-quality-baseline` into a project where both declared targets are absent
- **THEN** LunaPack creates `Directory.Build.props` and `Directory.Packages.props` and records both managed files for the installed pack

#### Scenario: Install a documentation template into an existing directory

- **WHEN** a user creates `docs/adr`, configures the repository local source, and runs `luna install madr-adr-template`
- **THEN** LunaPack creates `docs/adr/template.md` and records it as the pack's managed file

#### Scenario: Refuse an unknown or unavailable pack

- **WHEN** a user installs a pack that is not present in configured local sources
- **THEN** LunaPack returns a non-success result and does not change project files, configuration, or lock state

#### Scenario: Refuse to overwrite an existing target

- **WHEN** installation would write a declared target that already exists and is not recorded as managed by the same pack
- **THEN** LunaPack preserves the existing file, does not record the pack, and returns a non-success result

#### Scenario: Refuse duplicate installation

- **WHEN** a user installs a bundled pack after it is already recorded as a requested root pack
- **THEN** LunaPack leaves the project unchanged and returns a non-success result

### Requirement: Update installed root packs

LunaPack SHALL accept `luna update <pack-id>@<version>` to update an installed
requested root to an available explicit semantic version, and `luna update
<pack-id>` to update that root to the highest available semantic version from
the configured sources. `luna update` without a pack reference SHALL update
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

- **WHEN** a user runs `luna update dotnet-sdk-10` and a newer release exists
- **THEN** LunaPack applies the selected release's managed-file strategies and
  records the newest resolved version and target hashes

#### Scenario: Update a named pack to an explicit available version

- **WHEN** a user runs `luna update dotnet-sdk-10@1.2.0` for an installed root
  and that version is available
- **THEN** LunaPack installs that exact selected version and persists it as the
  root request

#### Scenario: Reject an update for an uninstalled root

- **WHEN** a user runs `luna update unknown-pack`
- **THEN** LunaPack returns a non-success result without changing project files or
  state

#### Scenario: Update all available installed roots

- **WHEN** a user runs `luna update` and multiple installed requested roots
  have newer available versions
- **THEN** LunaPack updates each eligible root and reports its selected newest
  version

### Requirement: Report outdated installed packs

LunaPack SHALL accept `luna outdated` and list every installed requested root
whose highest available configured-source version has greater semantic-version
precedence than its currently resolved version. Each result SHALL include the
pack ID, current version, and latest available version. When no requested root
is outdated, the command SHALL report that no updates are available and leave
the project unchanged.

#### Scenario: List available updates

- **WHEN** an installed root is at `1.0.0` and configured sources contain
  `1.1.0`
- **THEN** `luna outdated` reports that root with current version `1.0.0` and
  latest version `1.1.0`

### Requirement: Report outdated links

`luna outdated` SHALL evaluate installed links in addition to requested root
packs. A link SHALL be outdated when its definition digest, selected file digest,
selection, or mapped target changes. Each result SHALL identify the link name and
reasons. A Git source resolving to a different commit SHALL not by itself make a
link outdated.

#### Scenario: Report a newly matching file

- **WHEN** a file added to a source now matches an installed link include and is not excluded
- **THEN** `luna outdated` reports that link with an added-file reason

#### Scenario: Omit a content-equivalent new commit

- **WHEN** an installed Git link resolves to a different commit but its definition and complete selected-file result are unchanged
- **THEN** `luna outdated` does not report that link

### Requirement: Preview and confirm package changes

LunaPack SHALL accept `--dry-run` on `luna install` and every form of `luna
update`. A dry run SHALL perform source resolution and preflight, report each
planned target action and selected version, and SHALL not write, delete,
rename, or otherwise modify project files, `lunapack.yml`, or `lunapack-lock.yml`.

LunaPack SHALL accept `--prompt` on `luna update` without a pack reference. It
SHALL show each eligible pack and newest version, request confirmation before
that pack's update, update only confirmed packs, and leave declined packs
unchanged.

#### Scenario: Preview an install

- **WHEN** a user runs `luna install dotnet-sdk-10 --dry-run`
- **THEN** LunaPack reports the planned selected release and file actions without
  modifying files or state

#### Scenario: Preview an update

- **WHEN** a user runs `luna update dotnet-sdk-10 --dry-run`
- **THEN** LunaPack reports additions, removals, and strategy-driven changes
  without modifying files or state

#### Scenario: Confirm updates individually

- **WHEN** a user runs `luna update --prompt` and declines one of two
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

### Requirement: Select a cataloged version for installation

LunaPack SHALL accept one or more `luna install <pack-id>` or `luna install
<pack-id>@<version>` references. It SHALL resolve root pack candidates from the
configured source catalog. Without an explicit version, LunaPack SHALL select
the highest available semantic version according to Semantic Versioning
precedence. With an explicit version, LunaPack SHALL select that available
version. When candidates have equal version precedence, LunaPack SHALL select
the candidate from the earliest configured source. LunaPack SHALL record an
explicit root version request in `lunapack.yml` when one was supplied and SHALL
record all selected exact versions in `lunapack-lock.yml`. `luna install` SHALL
also accept `--dry-run` and report its resolved version and planned target
actions without mutation.

#### Scenario: Install an explicit version

- **WHEN** a configured local source catalogs multiple versions of a pack and a user runs `luna install <pack-id>@<version>` for one available version
- **THEN** LunaPack installs that version, records the root request in `lunapack.yml`, and records the selected version in `lunapack-lock.yml`

#### Scenario: Install the latest available version

- **WHEN** a configured local source catalogs multiple versions of a pack and a user runs `luna install <pack-id>` without a version
- **THEN** LunaPack installs the highest available version and records that selection in `lunapack-lock.yml`

#### Scenario: Reject an unavailable requested version

- **WHEN** a user requests a package version that is absent from the configured source catalog
- **THEN** LunaPack returns a non-success result, suggests the latest available
  version when the pack exists, and does not change project files,
  configuration, or lock state

#### Scenario: Skip an installed root in a multi-reference installation

- **WHEN** a user supplies an already installed requested root and another root
  to one `luna install` invocation
- **THEN** LunaPack warns that the installed root was skipped and continues
  processing the remaining references

#### Scenario: Prefer the earliest configured source for equal versions

- **WHEN** multiple configured sources provide the same package ID and version
- **THEN** LunaPack installs the candidate from the earliest configured source

#### Scenario: Preview a selected installation version

- **WHEN** a user runs `luna install <pack-id> --dry-run`
- **THEN** LunaPack reports the selected latest version and planned target actions
  without changing project files, configuration, or lock state

### Requirement: Safely uninstall an unchanged managed-file pack

The `luna uninstall <pack-id>` command SHALL remove every managed target and the requested-root record when every target content digest matches the digest recorded at installation. It SHALL not remove any managed content that differs from its recorded digest. It SHALL remove or retain transitive pack state according to whether those packs remain reachable from another requested root.

#### Scenario: Uninstall an unmodified single-file pack

- **WHEN** a user uninstalls `dotnet-gitignore` after its installed `.gitignore` remains unchanged
- **THEN** LunaPack removes `.gitignore`, its requested-root record, and its resolved lock record

#### Scenario: Uninstall an unmodified multi-file pack

- **WHEN** a user uninstalls `dotnet-quality-baseline` after both managed files remain unchanged
- **THEN** LunaPack removes both managed files and the corresponding pack record

#### Scenario: Preserve a modified managed file

- **WHEN** a user modifies a file installed by a bundled pack and runs `luna uninstall <pack-id>`
- **THEN** LunaPack preserves the modified file and project state and returns a non-success result

#### Scenario: Reject removal of an uninstalled pack

- **WHEN** a user runs `luna uninstall <pack-id>` without a corresponding requested-root record
- **THEN** LunaPack does not change project files or project state and returns a non-success result

### Requirement: Install a directly requested pack at a destination

LunaPack SHALL accept `luna install <pack-id> --destination <relative-directory>`
and apply the directory to every managed-file target owned directly by the
requested pack. It SHALL retain a pack manifest's declared targets when the
option is omitted. The destination SHALL be a non-empty path relative to the
project directory and SHALL not resolve outside it.

Dependencies resolved for the requested pack SHALL retain their declared
targets; the destination SHALL not relocate their managed files.

#### Scenario: Install a documentation pack into a selected directory

- **WHEN** a consumer installs a single-file documentation pack with
  `--destination docs/guidance`
- **THEN** LunaPack writes that file beneath `docs/guidance` and records its
  effective project-relative target

#### Scenario: Reject an unsafe destination

- **WHEN** a consumer supplies an absolute destination or one that resolves
  outside the project directory
- **THEN** LunaPack returns a non-success result and leaves project files and
  manifest state unchanged

#### Scenario: Preserve dependency targets

- **WHEN** a consumer installs a composite pack with a destination
- **THEN** any dependency-managed file uses its declared target rather than the
  composite pack's destination

### Requirement: Remap managed-file targets during installation

LunaPack SHALL resolve a managed file from its manifest-declared target to an effective project-relative target before preflight and mutation. `luna install` SHALL accept repeatable `--remap-directory <declared-directory>=<target-directory>` and `--remap-file <declared-file>=<target-file>` options. Global mappings in `lunapack.yml` SHALL apply to installations that do not have a higher-precedence command-line mapping. Exact file mappings SHALL take precedence over directory mappings; command-line mappings of the same kind SHALL take precedence over global mappings. A directory mapping SHALL retain the matched descendant suffix.

Every declared and effective target supplied by remapping SHALL be non-empty, project-relative, and contained within the project directory. LunaPack SHALL reject invalid mappings, duplicate mappings of the same scope and source, and target ownership or filesystem conflicts before changing project files, configuration, or lock state. `--destination` SHALL not be combined with either remapping option.

#### Scenario: Remap a manifest target directory at installation

- **WHEN** a consumer runs `luna install madr-adr-template --remap-directory docs/adr=docs/internal/01-architecture/decisions`
- **THEN** LunaPack writes `docs/internal/01-architecture/decisions/template.md` and records that effective target as managed by `madr-adr-template`

#### Scenario: Rename a single managed file at installation

- **WHEN** a consumer runs `luna install madr-adr-template --remap-file docs/adr/template.md=docs/adr/_template.md`
- **THEN** LunaPack writes and records `docs/adr/_template.md` as the managed target for the declared `docs/adr/template.md` file

#### Scenario: Retain descendants under a mapped directory

- **WHEN** a directory mapping redirects `docs/adr` and a pack manages `docs/adr/records/template.md`
- **THEN** LunaPack retains `records/template.md` beneath the configured effective directory

#### Scenario: Reject ambiguous destination and remapping options

- **WHEN** a consumer supplies `--destination` together with a remapping option
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

### Requirement: Preserve effective ownership during lifecycle operations

LunaPack SHALL use the effective managed-file targets recorded in `lunapack-lock.yml` for updates and uninstalls. An update SHALL apply retained managed files at their recorded effective targets and SHALL apply project-level remapping only to newly introduced declared targets. Changing global remapping after installation SHALL not relocate an already managed file; consumers SHALL use `luna mv` to relocate it explicitly.

#### Scenario: Update a remapped managed file

- **WHEN** an installed pack has a remapped managed target and a later selected release changes that managed file
- **THEN** `luna update` applies its strategy at the recorded remapped target

#### Scenario: Uninstall a remapped managed file

- **WHEN** an installed remapped managed file still matches its recorded digest and the consumer runs `luna uninstall` for its pack
- **THEN** LunaPack removes the file from its recorded remapped target

#### Scenario: Do not relocate an existing managed file after configuration change

- **WHEN** a consumer changes a global remap after the matching managed file is already installed and then updates its pack
- **THEN** LunaPack retains the existing recorded target instead of moving it

### Requirement: Move a managed file while retaining pack ownership

LunaPack SHALL provide `luna mv <source> <target>` to relocate exactly one lock-recorded managed file. Both paths SHALL be non-empty project-relative paths contained within the project directory. The command SHALL reject a source that is not a uniquely owned lock target, a target already owned by another managed file, or a state where both source and target files exist. When the source file exists and the target does not, LunaPack SHALL move the file and update its lock record atomically. When the source file does not exist but the target file exists, LunaPack SHALL update only the matching lock record to adopt the target path, retaining its recorded digest for later lifecycle protection.

#### Scenario: Move an installed ADR template

- **WHEN** `docs/adr/template.md` is a uniquely owned managed file and a user runs `luna mv docs/adr/template.md docs/architecture/adr/_template.md`
- **THEN** LunaPack moves the file to the target and records the target as the managed file location

#### Scenario: Rebind ownership to an already moved file

- **WHEN** a lock-recorded source is absent, its requested target exists, and a user runs `luna mv` with those paths
- **THEN** LunaPack leaves project files unchanged and updates the managed-file lock record to the target path

#### Scenario: Refuse an ambiguous move

- **WHEN** both the lock-recorded source and requested target files exist
- **THEN** LunaPack returns a non-success result without moving files or changing lock state

### Requirement: Adopt matching existing pack targets explicitly

LunaPack SHALL accept `--adopt-existing` during installation. It SHALL adopt an
existing unowned target only when every target to adopt has the exact SHA-256
digest of the source content that the pack would install. Without this option,
LunaPack SHALL retain its existing refusal to claim unowned targets.

If any proposed target differs from the source digest, LunaPack SHALL not copy,
delete, or claim any target and SHALL leave `lunapack.yml` and `lunapack-lock.yml`
unchanged.

#### Scenario: Adopt unchanged documentation

- **WHEN** a consumer installs a documentation pack with `--adopt-existing`
  and the existing target has the pack-content digest
- **THEN** LunaPack records ownership without replacing the target content

#### Scenario: Reject adoption of modified documentation

- **WHEN** a consumer installs a documentation pack with `--adopt-existing`
  and an existing target differs from the pack-content digest
- **THEN** LunaPack returns a non-success result without changing files or
  installation state

### Requirement: Persist destination selection

LunaPack SHALL preserve a directly requested pack's optional destination in both
`lunapack.yml` and `lunapack-lock.yml`, together with its resolved effective target
paths. A later uninstall SHALL remove the recorded effective targets after its
existing digest-protection checks pass.

#### Scenario: Remove a destination-installed pack

- **WHEN** a consumer uninstalls a destination-installed pack whose managed
  file still matches its recorded digest
- **THEN** LunaPack removes that file from its effective destination and removes
  the pack's destination metadata from both state files

### Requirement: Accept typed installation parameters

`luna install` SHALL accept repeatable `--parameter <name>=<value>` input and
the `-p` alias. LunaPack SHALL resolve each supplied value against the declaration
for that name, accepting boolean values only for `bool` parameters and exact
allowed values only for `enum` parameters. It SHALL reject malformed entries,
unknown names, duplicate command-line names, and incompatible values before
changing project files or state.

#### Scenario: Supply a required string parameter

- **WHEN** a user runs `luna install license-mit -p companyName=Lunaris`
- **THEN** LunaPack resolves `companyName` as the string `Lunaris` for that
  installation

#### Scenario: Reject an invalid enum value

- **WHEN** a user supplies a value not declared by an enum parameter
- **THEN** LunaPack returns a non-success result without copying files or changing
  installation state

### Requirement: Resolve project variables for pack parameters

Before validating required parameters, LunaPack SHALL bind a project variable with
the same name as a declared graph parameter unless `--no-variables` is present
or that name is supplied by repeatable `--skip-variable <name>`. Explicit
`--parameter` values SHALL take precedence over eligible project variables.
Variables that cannot be converted to the declared parameter type SHALL fail
installation before mutation.

#### Scenario: Bind a matching project variable

- **WHEN** `lunapack.yml` defines `companyName` and the installed pack declares
  a required `companyName` string parameter without an explicit value
- **THEN** LunaPack uses the project variable to satisfy the parameter

#### Scenario: Skip a matching project variable

- **WHEN** a user passes `--skip-variable companyName` and provides no other
  value for a required `companyName` parameter
- **THEN** LunaPack returns a non-success result for the missing required
  parameter without mutation

### Requirement: Resolve a composite graph's compatible parameter set

LunaPack SHALL collect parameter declarations from every pack in a resolved
installation graph before it validates inputs or plans managed files. For a
same-name declaration, the declaration nearest an installed root SHALL control
requiredness and enum values; all declarations SHALL retain the same type.
Composite reference bindings SHALL supply transient parameters when that name
is not declared by an installed root, and those values SHALL not be exposed to
or overridden by consumer input. Every remaining required graph parameter SHALL
have a resolved value from explicit input or an eligible project variable before
installation begins.

#### Scenario: Override a transient parameter declaration from the root

- **WHEN** a root and transient pack declare the same parameter with compatible
  types but different requiredness or enum values
- **THEN** LunaPack uses the root declaration without merging enum values

#### Scenario: Bind a hidden transient parameter from a composite reference

- **WHEN** a composite reference supplies a value for a parameter declared only
  by its transient pack
- **THEN** LunaPack uses that value without exposing it to consumer input

#### Scenario: Reject a type-changing composite parameter override

- **WHEN** same-name declarations in a resolved graph use different types
- **THEN** LunaPack returns a non-success result before copying files or changing
  project state

### Requirement: Preserve rendered-file ownership semantics

LunaPack SHALL compute adoption comparisons and lock-file SHA-256 digests from
the rendered content of every condition-selected managed file. Files excluded
by a false condition SHALL not be copied or recorded. Existing preflight,
conflict detection, transaction rollback, and uninstall protections SHALL
apply to the rendered selected-file set.

#### Scenario: Adopt a rendered matching target

- **WHEN** `--adopt-existing` is used and an existing target matches its
  rendered template output
- **THEN** LunaPack records the target as managed without replacing its content

#### Scenario: Reject a rendered content mismatch

- **WHEN** an existing target differs from its rendered template output
- **THEN** LunaPack does not claim, replace, or record that target

### Requirement: Render lifecycle script arguments

LunaPack SHALL render each lifecycle script argument as a strict Scriban
template using the resolved graph parameters before dry-run formatting, trust
authorization, confirmation, or execution. Each rendered list item SHALL remain
one process argument. `command`, `runner`, and packed `file` values SHALL remain
literal. An invalid argument template or unknown variable SHALL fail planning
before scripts execute or project files or state change.

#### Scenario: Pass a parameter to a lifecycle script

- **WHEN** a script argument references a resolved pack parameter
- **THEN** dry-run, consent, and execution use the rendered value as one argument

#### Scenario: Reject an unknown script parameter

- **WHEN** a script argument references an unknown parameter
- **THEN** LunaPack returns a non-success result before authorization or mutation

### Requirement: Authorize every lifecycle script before mutation

Before an install or update mutates managed files or project state, LunaPack SHALL build its complete applicable script plan for directly requested and transient packs and apply `--scripts <prompt|run|skip>`. The option SHALL default to `prompt`. `run` SHALL authorize every non-suppressed hook for that invocation without confirmation. `skip` SHALL execute no hooks and request no script confirmation. `prompt` SHALL authorize a hook only when its exact source identity is trusted, its source-identity-plus-pack-ID pair is trusted, or the user explicitly confirms it. Pack trust SHALL apply to every version of that ID from that exact source and SHALL not trust dependencies. Source trust SHALL apply only to packs resolved from that exact source identity.

For each script requiring consent, LunaPack SHALL show a structured confirmation containing the pack ID, hook type, optional description, and exact command and arguments that will run. LunaPack SHALL fail closed without executing scripts or mutating the operation when consent is declined, input is unavailable, or authorization cannot be established. `--dry-run` SHALL execute no scripts and SHALL report each planned hook with its selected consent mode.

#### Scenario: Confirm an untrusted script

- **WHEN** an interactive install resolves an untrusted `preInstall` script
- **THEN** LunaPack shows its pack ID, hook, optional description, and exact command and executes it only after confirmation

#### Scenario: Decline before project mutation

- **WHEN** a user declines any script in a pack's lifecycle plan
- **THEN** LunaPack executes no script and makes no managed-file or state change for that operation

#### Scenario: Deny an untrusted script without interactive input

- **WHEN** an install or update cannot prompt and an applicable script is not otherwise trusted
- **THEN** LunaPack returns a non-success result without executing the script or mutating that operation

#### Scenario: Run all scripts for one invocation

- **WHEN** a user runs `luna install <pack-id> --scripts run` or `luna update <pack-id> --scripts run`
- **THEN** every non-suppressed script in that command is authorized without a confirmation prompt

#### Scenario: Skip all scripts for one invocation

- **WHEN** a user runs install or update with `--scripts skip`
- **THEN** LunaPack applies the pack lifecycle without executing or prompting for any script

#### Scenario: Trust one pack without trusting its dependency

- **WHEN** a trusted root pack resolves an untrusted dependency that declares a lifecycle script
- **THEN** LunaPack still requires authorization for the dependency script

#### Scenario: Trust a resolved source

- **WHEN** a pack script comes from an exact configured-source identity present in effective trust
- **THEN** LunaPack authorizes that script without prompting

#### Scenario: Do not trust a rebound source name

- **WHEN** a trusted source name is changed to identify another location
- **THEN** LunaPack requires authorization because the configured-source identity no longer matches

#### Scenario: Preview lifecycle scripts

- **WHEN** a user runs an install or update with `--dry-run`
- **THEN** LunaPack lists applicable hooks and consent modes without executing any script or prompting for consent

### Requirement: Execute lifecycle scripts without an implicit shell

LunaPack SHALL start each authorized command as the declared executable with its declared arguments, and each authorized packed file as the declared runner with the confined file path followed by its declared arguments. It SHALL not pass either form through a command shell or interpret shell operators, substitutions, redirects, or environment expansion. It SHALL preserve each argument as one process argument. Before authorization, LunaPack SHALL materialize resolved pack content into an operation snapshot and bind packed files to that snapshot. A packed file SHALL resolve within its snapshot root after canonicalization; missing files, rooted paths, and traversal outside the root SHALL fail before execution. No-follow traversal of links and reparse points is not part of this contract while ADR-0040 remains active.

#### Scenario: Pass metacharacters literally

- **WHEN** a hook argument contains characters that a command shell would interpret
- **THEN** LunaPack passes them as literal content in one process argument

#### Scenario: Invoke a file shipped in the pack

- **WHEN** a hook declares a script file beneath the resolved pack root and an explicit runner
- **THEN** LunaPack starts the runner with the canonical snapshotted file path as one argument

#### Scenario: Reject a packed script path escape

- **WHEN** a hook identifies a pack-relative script file that resolves outside the pack root through traversal
- **THEN** LunaPack returns a non-success result before executing any script or mutating the pack

#### Scenario: Execute the content that was authorized

- **WHEN** source content changes after LunaPack builds and displays the script plan
- **THEN** LunaPack executes the previously materialized snapshot rather than the changed source file

#### Scenario: Report process-start failure

- **WHEN** the declared executable cannot be started
- **THEN** LunaPack reports the hook and pack ID and returns a non-success result

### Requirement: Apply transient lifecycle suppression

Every resolved transient pack SHALL participate in lifecycle planning and execute the same install or update hooks as a directly requested pack unless its incoming composite references suppress those lifecycle types. Suppression SHALL be expressed by lifecycle type, not script name. When the same transient pack is reachable through multiple references, LunaPack SHALL suppress the union of every incoming `disabledHooks` collection. Suppression on a transient reference SHALL not suppress hooks when that pack is also a directly requested root.

#### Scenario: Execute hooks for a transient installation

- **WHEN** installing a composite root introduces a transient pack with install hooks and no suppression
- **THEN** LunaPack authorizes and executes the transient pack's install hooks in graph order

#### Scenario: Suppress selected transient hooks

- **WHEN** a composite reference disables `preInstall` and `postInstall` for its referenced transient pack
- **THEN** LunaPack executes neither install hook for that transient pack

#### Scenario: Apply the most restrictive shared policy

- **WHEN** a shared transient pack has multiple incoming references with different disabled hooks
- **THEN** LunaPack suppresses every lifecycle type disabled by any incoming reference

#### Scenario: Preserve directly requested root hooks

- **WHEN** a pack is both directly requested and referenced transitively with disabled hooks
- **THEN** LunaPack does not apply transient-reference suppression to its directly requested root lifecycle

### Requirement: Run lifecycle hooks in deterministic phases

For a resolved graph, LunaPack SHALL run applicable hooks in stable dependency-first order. A newly installed pack SHALL use `preInstall` before managed-file mutation and `postInstall` after managed-file mutation. An already installed pack moving to a different resolved release SHALL use the incoming release's `preUpdate` and `postUpdate` hooks around its managed-file mutation. A newly introduced dependency during update SHALL use install hooks. Unchanged and removed packs SHALL run none of these hooks.

LunaPack SHALL persist configuration, lock state, and resulting managed-file digests only after all applicable post hooks for the operation succeed. A pre-hook failure SHALL prevent managed-file and state mutation. A post-hook failure SHALL return a non-success result and restore LunaPack-managed files, configuration, and lock state to their pre-operation state. LunaPack SHALL report that external side effects created by a script cannot be rolled back.

#### Scenario: Install a composite graph in dependency order

- **WHEN** a root and its dependency both declare install hooks
- **THEN** LunaPack executes dependency `preInstall` before root `preInstall`, applies the planned graph mutation, then executes dependency `postInstall` before root `postInstall`

#### Scenario: Update with incoming hooks

- **WHEN** an installed pack updates to a release that declares update hooks
- **THEN** LunaPack executes that incoming release's `preUpdate`, applies its managed-file update, and then executes its `postUpdate`

#### Scenario: Install a new dependency during update

- **WHEN** an update introduces a dependency that was not previously installed
- **THEN** the new dependency runs its install hooks rather than update hooks

#### Scenario: Stop after a pre-hook failure

- **WHEN** a pre-install or pre-update process exits unsuccessfully
- **THEN** LunaPack stops the operation without changing managed files, configuration, or lock state

#### Scenario: Restore managed state after a post-hook failure

- **WHEN** a post-install or post-update process exits unsuccessfully
- **THEN** LunaPack restores managed files, configuration, and lock state, reports the failed hook, and warns that external script side effects may remain

LunaPack SHALL preserve a private backup and exact-byte digest of `lunapack.yml` before the first hook. It SHALL not reload project configuration from disk during hook execution. Immediately after every hook process exits, LunaPack SHALL verify that `lunapack.yml` still exists and has the same exact bytes. If it differs or is missing, LunaPack SHALL log an error identifying the pack and hook, restore the original manifest bytes, abort before another hook runs, and roll back LunaPack-owned managed files, configuration, and lock state. A script that changes and restores the same bytes before exit is outside this detection guarantee.

#### Scenario: Abort after a hook changes project configuration

- **THEN** LunaPack restores the original bytes, logs an error, aborts immediately, and rolls back LunaPack-owned state

#### Scenario: Abort after a hook removes project configuration

- **WHEN** a lifecycle hook removes `lunapack.yml`
- **THEN** LunaPack restores the file and aborts before any later hook or state commit

LunaPack SHALL use each installed pack's locked configured-source identity when selecting update candidates for roots and transitive packs. An ordinary latest-version update SHALL not move a pack to another source. An explicit `luna update <pack-id>@<version>` MAY select that version from another configured source only when it is unavailable from the locked source. Before mutation or script authorization, LunaPack SHALL show the pack ID, old source identity, new source identity, and security consequence and require interactive source-switch confirmation. Declining or unavailable confirmation SHALL leave the graph unchanged. Trust for the old source or source-plus-pack pair SHALL not authorize scripts from the new source.

#### Scenario: Update from the locked source

- **WHEN** the locked source contains a newer eligible release

#### Scenario: Refuse implicit source movement

- **WHEN** another source contains a newer release but the locked source does not
- **THEN** an ordinary update leaves the pack current at its locked-source release

#### Scenario: Confirm an explicit source switch

- **WHEN** an explicit requested version is unavailable from the locked source and available from another configured source
- **THEN** LunaPack shows both exact identities and switches only after the user confirms

#### Scenario: Deny a non-interactive source switch

- **WHEN** an explicit update would switch sources and interactive confirmation is unavailable
- **THEN** LunaPack returns a non-success result without changing files or state

- **WHEN** a confirmed source switch selects a release with lifecycle hooks
- **THEN** LunaPack evaluates script trust against the new source identity

Completed non-dry-run lifecycle commands SHALL confirm their result and append
recommendations selected from persisted post-operation state. Guidance SHALL
use a concrete pack ID when the completed command supplies one.

- **WHEN** `luna install <pack-reference>` successfully installs a requested
  root pack

#### Scenario: Update installed packs

audit`and`luna outdated`

#### Scenario: Uninstall a pack while others remain

- **WHEN** `luna uninstall <pack-id>` succeeds and at least one requested root
  remains installed
- **THEN** Luna confirms the uninstalled pack ID and recommends `luna discover`
  and `luna install <pack>`

#### Scenario: Uninstall the last pack

- **WHEN** `luna uninstall <pack-id>` succeeds and no requested root remains
  installed
- **THEN** Luna confirms the uninstalled pack ID, reports that no packs are
  installed, and recommends `luna discover` and `luna search <keyword>`

### Requirement: Guide recovery from an unresolved installation

When installation cannot resolve a syntactically valid pack reference from
configured sources, Luna SHALL preserve its transactional non-success behavior
and append commands that help locate an available pack.

#### Scenario: Install an unknown pack

- **WHEN** a user runs `luna install unknown-pack` and no configured source
  provides that pack
- **THEN** Luna reports that `unknown-pack` was not found, recommends `luna
search unknown-pack` followed by `luna discover`, and leaves project files and
  state unchanged

### Requirement: Install links through the managed-file lifecycle

`luna install <name>` SHALL resolve a configured link when the name identifies a
link and no requested root pack with that ID is installed. Before mutation,
LunaPack SHALL resolve the source, effective Git ref when applicable, selectors,
safe targets, and content digests, then preflight the complete plan. It SHALL
copy selected files and persist link ownership and provenance atomically. Link
files SHALL use existing conflict and explicit adoption rules. LunaPack SHALL
reject duplicate link installation.

#### Scenario: Install a configured local link

- **WHEN** a user installs a valid local-source link whose targets pass preflight
- **THEN** LunaPack copies every selected file and records the link and per-file ownership atomically

#### Scenario: Install a configured Git link

- **WHEN** a user installs a valid Git-source link
- **THEN** LunaPack copies files from one resolved commit and records that commit with per-file ownership

#### Scenario: Refuse a conflicting link installation

- **WHEN** a selected target contains unowned content or belongs to another root and no supported explicit adoption applies
- **THEN** LunaPack returns a non-success result without changing files, configuration, or lock state

### Requirement: Process ordered typed lifecycle hooks

LunaPack SHALL include every applicable typed hook from directly requested and transient packs in the lifecycle plan. It SHALL authorize all `script` hooks before processing any hook, preserve existing script trust and execution requirements, process pack events in the existing stable dependency-first lifecycle order, and process each event's hooks in manifest order. Composite-reference `disabledHooks` SHALL suppress every script and instruction hook for each named event. Incoming update releases and dependencies newly introduced by an update SHALL retain the existing event-selection rules.

#### Scenario: Process mixed hooks in declared order

- **WHEN** one `preInstall` event declares an instruction, a script, and another instruction in that order
- **THEN** LunaPack displays, executes, and displays those hooks in the declared order after all applicable scripts have been authorized

#### Scenario: Suppress every typed hook for an event

- **WHEN** a composite reference disables `postInstall` for a transient pack whose event contains script and instruction hooks
- **THEN** LunaPack processes none of that pack's `postInstall` hooks

#### Scenario: Preserve script trust enforcement

- **WHEN** an ordered hook list contains an untrusted script and an instruction
- **THEN** LunaPack requires script authorization before displaying the instruction or changing project files or state

### Requirement: Allow consumers to skip instruction hooks

`luna install` and every form of `luna update` SHALL accept `--skip-instructions`. When present, LunaPack SHALL not load, render, parse, display, or prompt for any instruction hook in that invocation. The option SHALL not suppress script hooks or change `--scripts <prompt|run|skip>` behavior. Without `--skip-instructions`, applicable instruction hooks SHALL be processed.

#### Scenario: Skip instructions without skipping scripts

- **WHEN** a user runs install or update with `--skip-instructions` for a pack containing script and instruction hooks
- **THEN** LunaPack processes script hooks under the selected script-consent mode and processes no instruction hooks

#### Scenario: Display instructions by default

- **WHEN** a user runs install or update without `--skip-instructions`
- **THEN** LunaPack processes every applicable non-suppressed instruction hook

### Requirement: Validate and summarize instruction hooks during dry run

With `--dry-run`, LunaPack SHALL load, render, and parse applicable instruction hooks so invalid content fails planning. It SHALL list each planned instruction hook with its pack ID, lifecycle event, file, templating state, and detected step count, but SHALL not enter guided step display or request instruction confirmation.

#### Scenario: Preview a valid instruction hook

- **WHEN** a user runs install or update with `--dry-run` and an applicable instruction hook is valid
- **THEN** LunaPack reports the hook and detected step count without displaying its guided flow or prompting

#### Scenario: Reject an invalid instruction during dry run

- **WHEN** a dry run encounters a missing instruction file or invalid enabled template
- **THEN** LunaPack returns a non-success result without executing hooks or changing project files or state
