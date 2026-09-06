## MODIFIED Requirements

### Requirement: Reject invalid composite graphs before installation

LunaPack SHALL reject one requested-root instance graph that resolves the same pack ID to conflicting versions or declares managed targets that conflict with another installed or planned owner. Different requested-root instances MAY resolve different versions of the same root or transitive pack, subject to target-ownership rules. LunaPack SHALL ignore a pack's reference to itself and any reference edge that closes a direct or transitive dependency cycle, emit a warning identifying the ignored edge, and continue resolving the remaining graph. LunaPack SHALL validate the resulting acyclic graph before writing managed files or persisting project state.

#### Scenario: Ignore a self-reference

- **WHEN** `pack-a` directly references `pack-a`
- **THEN** LunaPack warns that the self-reference was ignored and continues resolving `pack-a` without that edge

#### Scenario: Refuse a composite dependency cycle

- **WHEN** resolution visits `pack-a`, `pack-b`, and `pack-c` and encounters the edge from `pack-c` back to `pack-a`
- **THEN** LunaPack warns that the cycle-closing edge was ignored and continues with the effective graph `pack-a` to `pack-b` to `pack-c`

#### Scenario: Refuse conflicting target ownership

- **WHEN** two packs or instances in a resolved lifecycle plan declare the same effective target path
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

#### Scenario: Refuse conflicting dependency versions within one instance

- **WHEN** one requested-root instance graph requires incompatible versions of the same transitive pack
- **THEN** LunaPack returns a non-success result without changing project files, configuration, or lock state

#### Scenario: Allow different versions across sibling instances

- **WHEN** two requested-root instances resolve different versions of the same pack without target-ownership conflicts
- **THEN** LunaPack retains both exact resolutions without treating their versions alone as a conflict

### Requirement: Discover and install bundled managed-file packs

LunaPack SHALL provide versioned `dotnet-gitignore`, `dotnet-sdk-10`,
`editorconfig-baseline`, `dotnet-csharp-editorconfig`, `csharpier`,
`dotnet-build-config`, `dotnet-central-package-management`, and `madr-template`
managed-file packs in the repository's local pack source. It SHALL also provide
the `dotnet-editorconfig` and `dotnet-project` composite packs with exact
references to their component packs.

The `luna install <pack-id>` command SHALL discover each bundled pack from configured local sources, overwrite unowned existing targets selected by the installation, and record the instance identity, resolved version, source, managed target paths, and installed content digests in `lunapack-lock.yml`. A target owned by another pack instance or link SHALL remain a conflict.

#### Scenario: Install a single-file bundled pack from a configured local source

- **WHEN** a user initializes an otherwise empty project, adds the repository local pack source, and runs `luna install dotnet-editorconfig`
- **THEN** the project contains the pack's `.editorconfig` content and `lunapack.yml` records the installed pack instance and managed file

#### Scenario: Install the .NET project profile

- **WHEN** a user installs `dotnet-project` into a project where both component targets are absent
- **THEN** LunaPack creates `Directory.Build.props` through `dotnet-build-config`, creates `Directory.Packages.props` through `dotnet-central-package-management`, and records the root instance and both component owners

#### Scenario: Install a documentation template into an existing directory

- **WHEN** a user creates `docs/adr`, configures the repository local source, and runs `luna install madr-template`
- **THEN** LunaPack creates `docs/adr/template.md` and records it as the pack instance's managed file

#### Scenario: Refuse an unknown or unavailable pack

- **WHEN** a user installs a pack that is not present in configured local sources
- **THEN** LunaPack returns a non-success result and does not change project files, configuration, or lock state

#### Scenario: Refuse to overwrite an existing target

- **WHEN** installation selects a target that exists but has no LunaPack owner
- **THEN** LunaPack no longer refuses solely because the target exists; it overwrites the target, records the installed content digest and instance ownership, and completes without an interactive conflict prompt

#### Scenario: Refuse an owned existing target

- **WHEN** installation selects a target owned by another pack instance or link
- **THEN** LunaPack preserves the target and returns a non-success result without changing project state

#### Scenario: Refuse duplicate installation

- **WHEN** a user installs a pack whose alias or canonical effective target mapping duplicates an existing instance of that pack
- **THEN** LunaPack leaves the project unchanged and returns a non-success result

### Requirement: Select a cataloged version for installation

LunaPack SHALL accept one or more `luna install <pack-id>` or `luna install
<pack-id>@<version>` references. It SHALL resolve root pack candidates from the
configured source catalog. Without an explicit version, LunaPack SHALL select
the highest available semantic version according to Semantic Versioning
precedence. With an explicit version, LunaPack SHALL select that available
version. When candidates have equal version precedence, LunaPack SHALL select
the candidate from the earliest configured source. LunaPack SHALL record an
explicit root version request in `lunapack.yml` when one was supplied and SHALL
record all selected exact versions and instance identities in
`lunapack-lock.yml`. `luna install` SHALL also accept `--dry-run` and report its
resolved version, instance alias, and planned target actions without mutation.

#### Scenario: Install an explicit version

- **WHEN** a configured local source catalogs multiple versions of a pack and a user runs `luna install <pack-id>@<version>` for one available version
- **THEN** LunaPack installs that version, records the root instance request in `lunapack.yml`, and records the selected version in `lunapack-lock.yml`

#### Scenario: Install the latest available version

- **WHEN** a configured local source catalogs multiple versions of a pack and a user runs `luna install <pack-id>` without a version
- **THEN** LunaPack installs the highest available version and records that selection for the default instance in `lunapack-lock.yml`

#### Scenario: Reject an unavailable requested version

- **WHEN** a user requests a package version that is absent from the configured source catalog
- **THEN** LunaPack returns a non-success result, suggests the latest available version when the pack exists, and does not change project files, configuration, or lock state

#### Scenario: Skip an installed root in a multi-reference installation

- **WHEN** a user supplies an already installed default instance and another root to one `luna install` invocation
- **THEN** LunaPack warns that the installed instance was skipped and continues processing the remaining references

#### Scenario: Prefer the earliest configured source for equal versions

- **WHEN** multiple configured sources provide the same package ID and version
- **THEN** LunaPack installs the candidate from the earliest configured source

#### Scenario: Preview a selected installation version

- **WHEN** a user runs `luna install <pack-id> --dry-run`
- **THEN** LunaPack reports the selected latest version, default instance alias, and planned target actions without changing project files, configuration, or lock state

### Requirement: Safely uninstall an unchanged managed-file pack

The `luna uninstall <pack-id>` command SHALL select one requested-root instance, remove only that instance's managed targets and requested-root record when every target content digest matches the digest recorded at installation, and preserve every other instance. It SHALL accept `--name <alias>` to select a specific instance. It SHALL not remove managed content that differs from its recorded digest. It SHALL remove or retain shared transitive pack state according to whether those packs remain reachable from another requested-root instance.

#### Scenario: Uninstall an unmodified single-file pack

- **WHEN** a user uninstalls the sole `dotnet-gitignore` instance after its installed `.gitignore` remains unchanged
- **THEN** LunaPack removes `.gitignore`, its requested-root instance record, and any resolved lock record no longer reachable from another instance

#### Scenario: Uninstall one named instance

- **WHEN** `orders` and `customers` instances of `dotnet-api` exist and a user runs `luna uninstall dotnet-api --name orders`
- **THEN** LunaPack removes only unchanged files owned by `orders`, preserves `customers`, and retains shared dependencies still reachable from `customers`

#### Scenario: Uninstall an unmodified composite profile

- **WHEN** a user uninstalls the selected `dotnet-project` instance after both component-managed files remain unchanged and no other requested-root instance references its components
- **THEN** LunaPack removes both managed files and the selected instance and unreachable component records

#### Scenario: Preserve a modified managed file

- **WHEN** a user modifies a file installed by a pack instance and uninstalls that instance
- **THEN** LunaPack preserves the modified file and project state and returns a non-success result

#### Scenario: Reject removal of an uninstalled pack

- **WHEN** a user supplies a pack ID and alias without a corresponding requested-root instance
- **THEN** LunaPack does not change project files or project state and returns a non-success result

## ADDED Requirements

### Requirement: Identify requested-root pack instances

LunaPack SHALL identify each requested-root instance by its pack ID and alias. `luna install <pack-id> --name <alias>` SHALL create a named instance, while omission of `--name` SHALL use the pack ID as the default alias. Aliases SHALL use pack-ID syntax and SHALL be unique, with exact case-sensitive comparison, among instances of the same pack. `--name` SHALL require exactly one pack reference. Existing packs SHALL support instances without manifest changes.

#### Scenario: Install a default instance

- **WHEN** a user installs `dotnet-api` without `--name` and no default instance exists
- **THEN** LunaPack creates an instance identified by pack ID `dotnet-api` and alias `dotnet-api`

#### Scenario: Reuse an alias for another pack

- **WHEN** `pack-a` has alias `orders` and a user installs `pack-b --name orders`
- **THEN** LunaPack allows the alias because uniqueness is scoped to the pack ID

#### Scenario: Reject a duplicate alias for one pack

- **WHEN** `dotnet-api` already has alias `orders` and a user installs `dotnet-api --name orders`
- **THEN** LunaPack returns a non-success result without changing project files or state

#### Scenario: Reject one alias for multiple install references

- **WHEN** a user supplies `--name` with more than one pack reference
- **THEN** LunaPack returns a non-success result without resolving or installing any reference

### Requirement: Require distinct instance placement

When another instance of the same pack already exists, LunaPack SHALL require the candidate instance to resolve at least one manifest-declared target to a different effective project-relative path. LunaPack SHALL compare canonical declared-to-effective target mappings after path normalization and all destination and remapping precedence rules; `--destination` and file or directory remaps MAY provide the difference, while explicit identity mappings SHALL not. LunaPack SHALL reject equal canonical mappings before mutation. Any valid project-relative target location is allowed, subject to existing path-safety and ownership rules.

#### Scenario: Install a second instance with a directory remap

- **WHEN** the default `dotnet-api` instance maps `src/Api` to `src/Api` and `orders` maps it to `src/OrdersApi`
- **THEN** LunaPack installs `orders` as a distinct instance

#### Scenario: Install a second instance with a destination

- **WHEN** one instance uses declared targets and another instance applies a destination that changes at least one effective target
- **THEN** LunaPack treats their canonical effective target mappings as distinct

#### Scenario: Reject an identity remap as a distinction

- **WHEN** an additional instance explicitly remaps every declared target to the same effective path used by an existing instance
- **THEN** LunaPack returns a non-success result without changing project files or state

#### Scenario: Reject another instance of a contentless pack

- **WHEN** a pack has no manifest-declared targets and one instance is already installed
- **THEN** LunaPack rejects another instance because no effective target can distinguish its placement

### Requirement: Resolve instance values independently

LunaPack SHALL resolve command inputs, project variables, parameter prompts, defaults, conditions, and templates separately for each requested-root instance. Values selected for one instance SHALL not propagate to a sibling instance. Transitive resolution nodes SHALL be shared only when pack identity, exact version, source, and resolved state match; otherwise LunaPack SHALL retain distinct nodes and apply normal target-ownership conflict checks without creating an instance-to-instance dependency graph.

#### Scenario: Render sibling instances with different parameters

- **WHEN** `orders` resolves `ServiceName=Orders` and `customers` resolves `ServiceName=Customers`
- **THEN** each instance renders and records only the files produced from its own resolved values

#### Scenario: Keep distinct dependency resolutions

- **WHEN** a new root instance resolves a different exact version or source state for a transitive pack
- **THEN** LunaPack retains a distinct transitive node and succeeds only if its effective targets do not conflict with existing ownership

### Requirement: Select instances for lifecycle operations

`luna update <pack-id>` and `luna uninstall <pack-id>` SHALL select the sole installed instance when exactly one exists, otherwise select the instance whose alias equals the pack ID when present. When multiple instances exist and none has the default alias, the command SHALL return a non-success result and require `--name`. Supplying `--name <alias>` SHALL select that exact instance of the specified pack. Update-all and outdated operations SHALL evaluate every requested-root instance independently and identify results by pack ID and alias.

#### Scenario: Select the sole named instance

- **WHEN** `orders` is the only installed `dotnet-api` instance and a user runs `luna update dotnet-api`
- **THEN** LunaPack updates `orders`

#### Scenario: Prefer the default instance

- **WHEN** `dotnet-api` and `orders` instances exist and a user runs `luna update dotnet-api`
- **THEN** LunaPack updates the instance whose alias is `dotnet-api`

#### Scenario: Require a name for ambiguous instances

- **WHEN** `orders` and `customers` instances exist without a `dotnet-api` alias and a user runs `luna update dotnet-api`
- **THEN** LunaPack returns a non-success result, lists the matching aliases, and instructs the user to specify `--name`

#### Scenario: Update a named instance

- **WHEN** a user runs `luna update dotnet-api --name orders`
- **THEN** LunaPack updates only the `orders` instance and its ownership records

### Requirement: Rename a pack instance

`luna rename <pack-id> --name <alias> --to <new-alias>` SHALL atomically change the selected requested-root instance alias in portable configuration and resolved lock state without changing its files, digests, placement, or ownership. The new alias SHALL satisfy instance alias syntax and SHALL not equal another alias for the same pack. Renaming to the pack ID SHALL establish that instance as the default.

#### Scenario: Rename a named instance

- **WHEN** a user runs `luna rename dotnet-api --name orders --to order-service`
- **THEN** configuration and lock state identify the same owned files as `order-service` and no longer contain the `orders` instance alias

#### Scenario: Reject a duplicate rename target

- **WHEN** `customers` already identifies a `dotnet-api` instance and a user renames another `dotnet-api` instance to `customers`
- **THEN** LunaPack returns a non-success result without changing configuration, lock state, or files

### Requirement: Overwrite only unowned lifecycle targets

Install and update operations SHALL apply their selected overwrite strategy to existing unowned files without an interactive conflict workflow. They SHALL reject any effective target owned by another pack instance or link and SHALL not transfer or merge ownership implicitly.

#### Scenario: Update into an unowned new target

- **WHEN** an updated release introduces a target that already exists without LunaPack ownership
- **THEN** LunaPack overwrites the target and records it under the updated instance

#### Scenario: Preserve another instance's target

- **WHEN** installation or update would write a target owned by another instance
- **THEN** LunaPack returns a non-success result without changing either instance or the target
