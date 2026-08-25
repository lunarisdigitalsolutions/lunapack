## ADDED Requirements

### Requirement: Resolve composite pack references from configured sources

LunaPack SHALL recursively resolve every composite pack reference from the local
sources configured in the consuming project's `lunapack.yml`. Each composite
reference SHALL resolve the declared ID and exact version using the same
source-precedence rules as direct installation. LunaPack SHALL not read source
configuration from a pack manifest.

#### Scenario: Install a composite pack from configured sources

- **WHEN** a user installs a composite pack whose referenced packs are present
  in configured sources
- **THEN** LunaPack resolves and installs the composite pack, all references, and
  their managed files

#### Scenario: Resolve a composite reference from the earliest configured source

- **WHEN** equal ID-and-version composite candidates exist in multiple
  configured sources
- **THEN** LunaPack selects the candidate from the earliest configured source

#### Scenario: Refuse a missing composite reference

- **WHEN** a composite pack references an ID and version absent from configured
  sources
- **THEN** LunaPack returns a non-success result without changing project files,
  configuration, or lock state

### Requirement: Reject invalid composite graphs before installation

LunaPack SHALL reject a composite graph that contains a dependency cycle, resolves
the same pack ID to conflicting versions, or declares managed targets that
conflict with another pack in the graph or existing unowned project files.
LunaPack SHALL validate the complete graph before writing managed files or
persisting project state.

#### Scenario: Refuse a composite dependency cycle

- **WHEN** composite references form a direct or transitive cycle
- **THEN** LunaPack returns a non-success result without changing project files,
  configuration, or lock state

#### Scenario: Refuse conflicting target ownership

- **WHEN** two packs in a resolved composite graph declare the same target path
- **THEN** LunaPack returns a non-success result without changing project files,
  configuration, or lock state

## MODIFIED Requirements

### Requirement: Discover and install the dotnet gitignore pack

LunaPack SHALL provide a versioned `dotnet-gitignore` pack in the repository's
local pack source. The pack SHALL declare one managed file that supplies
`.gitignore` content suitable for a .NET project.

The `lunapack install dotnet-gitignore` command SHALL discover the pack from
configured local sources, create the declared `.gitignore` target, add the pack
as a requested root in `lunapack.yml`, and record the resolved pack identity,
version, source provenance, managed target path, and installed content digest
in `lunapack-lock.yml`.

#### Scenario: Install the pack from a configured local source

- **WHEN** a user initializes a project, adds the repository local pack source,
  and runs `lunapack install dotnet-gitignore`
- **THEN** the project contains the pack's `.gitignore` content, `lunapack.yml`
  records the requested root pack, and `lunapack-lock.yml` records resolved
  ownership state

#### Scenario: Refuse an unknown or unavailable pack

- **WHEN** a user installs a pack that is not present in configured local sources
- **THEN** LunaPack returns a non-success result and does not change project files,
  configuration, or lock state

#### Scenario: Refuse to overwrite an existing target

- **WHEN** installation would write `.gitignore` that already exists and is not
  recorded as managed by the same resolved pack
- **THEN** LunaPack preserves the existing file, does not record the pack, and
  returns a non-success result

#### Scenario: Refuse duplicate installation

- **WHEN** a user installs `dotnet-gitignore` after it is already recorded as a
  requested root pack
- **THEN** LunaPack leaves the project unchanged and returns a non-success result

### Requirement: Select a cataloged version for installation

LunaPack SHALL accept both `lunapack install <pack-id>` and
`lunapack install <pack-id>@<version>`. It SHALL resolve root pack candidates
from the configured source catalog. Without an explicit version, LunaPack SHALL
select the highest available semantic version according to Semantic Versioning
precedence. With an explicit version, LunaPack SHALL select that available
version. When candidates have equal version precedence, LunaPack SHALL select the
candidate from the earliest configured source. LunaPack SHALL record an explicit
root version request in `lunapack.yml` when one was supplied and SHALL record all
selected exact versions in `lunapack-lock.yml`.

#### Scenario: Install an explicit version

- **WHEN** a configured local source catalogs multiple versions of a pack and a
  user runs `lunapack install <pack-id>@<version>` for one available version
- **THEN** LunaPack installs that version, records the root request in `lunapack.yml`,
  and records the selected version in `lunapack-lock.yml`

#### Scenario: Install the latest available version

- **WHEN** a configured local source catalogs multiple versions of a pack and a
  user runs `lunapack install <pack-id>` without a version
- **THEN** LunaPack installs the highest available version and records that
  selection in `lunapack-lock.yml`

#### Scenario: Reject an unavailable requested version

- **WHEN** a user requests a package version that is absent from the configured
  source catalog
- **THEN** LunaPack returns a non-success result and does not change project files,
  configuration, or lock state

#### Scenario: Prefer the earliest configured source for equal versions

- **WHEN** multiple configured sources provide the same package ID and version
- **THEN** LunaPack installs the candidate from the earliest configured source

### Requirement: Safely uninstall the dotnet gitignore pack

The `lunapack uninstall dotnet-gitignore` command SHALL remove the managed
`.gitignore` target and the requested-root record when the target content
matches the digest recorded in `lunapack-lock.yml`. It SHALL not remove content
that differs from the recorded digest. It SHALL remove or retain transitive
pack state according to whether those packs remain reachable from another
requested root.

#### Scenario: Uninstall an unmodified managed file

- **WHEN** a user uninstalls `dotnet-gitignore` after its installed `.gitignore`
  remains unchanged
- **THEN** LunaPack removes `.gitignore`, its requested-root record, and its
  resolved lock record

#### Scenario: Preserve a modified managed file

- **WHEN** a user modifies the `.gitignore` installed by `dotnet-gitignore` and
  runs `lunapack uninstall dotnet-gitignore`
- **THEN** LunaPack preserves the modified file and project state and returns a
  non-success result

#### Scenario: Reject removal of an uninstalled pack

- **WHEN** a user runs `lunapack uninstall dotnet-gitignore` without a corresponding
  requested-root record
- **THEN** LunaPack does not change project files or project state and returns a
  non-success result
