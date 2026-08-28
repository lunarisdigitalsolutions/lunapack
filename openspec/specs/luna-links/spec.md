# Luna Links Specification

## Purpose

Define project-owned selections of files from configured sources and manage those files through LunaPack's protected lifecycle without requiring an upstream pack manifest.

## Requirements

### Requirement: Add a project-owned link definition

`luna links add <name>` SHALL create a link in the selected workspace's `lunapack.yml`. The command SHALL require `--source <name>` and at least one repeatable `--include <pattern>`. It SHALL accept repeatable `--exclude <pattern>`, `--path <path>`, `--target <path>`, `--ref <git-ref>`, `--strip-prefix <path>`, `--flatten`, `--install`, `--force`, and `--workspace <path>` options, with `-s`, `-i`, `-e`, `-t`, and `-w` aliases. The name SHALL use LunaPack pack-ID syntax and SHALL not equal another link name or an installed root pack ID. The source SHALL identify an existing configured local or Git source. Without `--force`, an existing link definition SHALL be preserved. Replacing a definition SHALL not mutate installed files or resolved lock state unless `--install` also completes successfully.

#### Scenario: Add a link for one source file

- **WHEN** a user runs `luna links add copilot-instructions --source awesome-copilot --include instructions/dotnet.instructions.md`
- **THEN** LunaPack records the named source and include selector in `lunapack.yml` without installing content

#### Scenario: Add and install a link

- **WHEN** a user supplies a valid new link definition with `--install`
- **THEN** LunaPack persists the definition and installs its selected files as one atomic lifecycle operation

#### Scenario: Reject an incomplete link

- **WHEN** a user omits the source or supplies no include selector
- **THEN** LunaPack returns a non-success result without changing configuration, lock state, or project files

#### Scenario: Replace a definition without changing installed content

- **WHEN** a user replaces an existing link definition with `--force` and omits `--install`
- **THEN** LunaPack persists the new definition and leaves existing managed files and resolved lock state unchanged

#### Scenario: Reject a conflicting link name

- **WHEN** a proposed link name equals another link name or an installed root pack ID
- **THEN** LunaPack returns a non-success result without changing project state

### Requirement: Document links from a public GitHub repository

The developer documentation SHALL demonstrate that a consumer can register `github/awesome-copilot` as a GitHub source and manage agent files from its `agents` directory without an upstream LunaPack manifest. The documentation SHALL include one exact-file link and one glob-selected link, and SHALL describe the resulting roots as Luna Links rather than published packs.

#### Scenario: Document one Awesome Copilot agent file

- **WHEN** a consumer reads the Luna Links documentation
- **THEN** it includes `luna sources add github awesome-copilot github/awesome-copilot` followed by `luna links add agents-csharp-expert --source awesome-copilot --path agents --include CSharpExpert.agent.md --target .github/agents --install`
- **AND** it explains that the link installs `CSharpExpert.agent.md` into `.github/agents`

#### Scenario: Document an Awesome Copilot agent glob

- **WHEN** a consumer reads the Luna Links documentation
- **THEN** it includes `luna links add agents-ai-team --source awesome-copilot --path agents --include "ai-team*.agent.md" --target .github/agents --install`
- **AND** it explains that the link installs every matching AI-team agent file into `.github/agents`

### Requirement: Select source files deterministically

Each include value SHALL resolve as an exact file, a directory selected recursively, or a glob pattern. Selectors SHALL be evaluated relative to the link's base `path`, or relative to the configured source root when `path` is omitted. LunaPack SHALL union all include matches, remove duplicate source paths, and then apply all exclusion glob patterns. The final selection SHALL contain regular files only and SHALL be ordered by normalized ordinal source path. LunaPack SHALL reject a selection that is empty, cannot be evaluated, or resolves outside the configured source root.

#### Scenario: Combine directory and glob selectors

- **WHEN** a link includes `instructions` and `prompts/**/*.prompt.md`
- **THEN** LunaPack selects every regular file under `instructions` recursively plus every matching prompt file exactly once

#### Scenario: Apply exclusions after includes

- **WHEN** included files also match `**/experimental/**` or `**/README.md` exclusions
- **THEN** LunaPack removes those files from the combined selection

#### Scenario: Reject an empty selection

- **WHEN** no regular file remains after include and exclude evaluation
- **THEN** LunaPack returns a non-success result without changing managed files, configuration, or lock state

#### Scenario: Reject source traversal

- **WHEN** a base path, exact include, directory include, or glob match resolves outside the configured source root
- **THEN** LunaPack returns a non-success result before reading or copying that content

### Requirement: Map selected files to safe targets

LunaPack SHALL map each selected source path beneath the optional target directory while preserving its path relative to the link base. When `target` is omitted, the workspace root SHALL be used. When `strip-prefix` is present, every selected relative source path SHALL begin with that complete normalized path prefix and LunaPack SHALL remove it before applying the target. When `flatten` is present, LunaPack SHALL use only each selected file's name beneath the target. Project file and directory remappings SHALL apply to link targets, with exact file mappings and command-line mappings taking precedence as they do for pack targets. Updates and outdated checks SHALL retain each existing selected source path's lock-recorded effective target and SHALL apply current project mappings only to newly selected source paths. `luna install <link> --save-remap` SHALL persist supplied command-line mappings only after a successful installation. Base, target, prefix, source, and resulting paths SHALL accept either directory separator at input boundaries and SHALL be persisted with `/`. LunaPack SHALL reject rooted paths, traversal outside the source or workspace, an incompatible strip prefix, duplicate effective targets, and ownership or filesystem conflicts before mutation.

#### Scenario: Preserve selected source hierarchy

- **WHEN** a link targets `.github` and selects `collections/prompts/review.prompt.md` relative to its base
- **THEN** LunaPack maps the file to `.github/collections/prompts/review.prompt.md`

#### Scenario: Strip a common source prefix

- **WHEN** every selected path begins with `collections` and the link uses `--strip-prefix collections --target .github`
- **THEN** LunaPack removes `collections` and maps the remaining relative paths beneath `.github`

#### Scenario: Flatten selected files

- **WHEN** a link uses `--flatten --target .github` and selected files have distinct names
- **THEN** LunaPack maps every file directly beneath `.github`

#### Scenario: Reject a flatten collision

- **WHEN** two selected files have the same file name and flattening maps them to one target
- **THEN** LunaPack returns a non-success result without changing managed files or project state

#### Scenario: Reuse a saved link remapping

- **WHEN** a link install saves a command-line mapping and the link is later installed without command-line mappings
- **THEN** LunaPack applies the persisted project mapping to the selected link targets

#### Scenario: Retain an installed link target after mapping changes

- **WHEN** a consumer changes a project mapping after a link file is installed and checks or updates the link
- **THEN** LunaPack retains that file's lock-recorded effective target and applies the changed mapping only to newly selected files

#### Scenario: Reject an escaping target

- **WHEN** a target or calculated effective path resolves outside the workspace
- **THEN** LunaPack returns a non-success result without changing managed files or project state

### Requirement: Inspect configured links

`luna links list` SHALL list every configured link with its name, source, target, and installation status. `luna links show <name>` SHALL show its source, effective Git ref when applicable, resolved commit when installed from Git, base path, includes, excludes, target, installation status, selected-file count, and locally modified-file count. An omitted target SHALL be displayed as the workspace root. Inspection commands SHALL not mutate project state.

#### Scenario: List installed and uninstalled links

- **WHEN** a project has one installed link and one definition without lock state
- **THEN** `luna links list` identifies each link and reports its corresponding installation status

#### Scenario: Show an installed Git link

- **WHEN** a user shows an installed Git-backed link
- **THEN** LunaPack displays its configured selection, effective ref, locked commit, selected-file count, and count of targets whose current digest differs from lock state

#### Scenario: Reject an unknown link inspection

- **WHEN** a user runs `luna links show <name>` for an unconfigured link
- **THEN** LunaPack returns a non-success result without changing project state

### Requirement: Remove link definitions safely

`luna links rm <name>` SHALL refuse to remove a link that has installed lock ownership and SHALL direct the user to run `luna uninstall <name>` first. The command SHALL remove an uninstalled definition without changing unrelated state. With `--force`, the command SHALL remove the definition and its link lock records, SHALL delete only unchanged files owned exclusively by that link, and SHALL preserve every locally modified file. The command SHALL use exact case-sensitive name matching.

#### Scenario: Remove an uninstalled link

- **WHEN** a user removes a configured link with no resolved lock state
- **THEN** LunaPack removes its definition and preserves all other project state

#### Scenario: Refuse normal removal of an installed link

- **WHEN** a user removes a link that still owns installed files without `--force`
- **THEN** LunaPack preserves the definition and lock state and tells the user to run `luna uninstall <name>` first

#### Scenario: Force removal with a locally modified file

- **WHEN** a forced removal encounters a target whose current digest differs from its locked digest
- **THEN** LunaPack preserves that target while removing the link definition and all ownership records atomically

### Requirement: Exclude pack-only behavior from links

A link definition SHALL NOT support parameters, templates, lifecycle scripts, references to packs or links, publication as a reusable pack, or operating-system symbolic links. Unknown or pack-only properties in persisted link configuration SHALL make project configuration invalid.

#### Scenario: Reject pack behavior in a link definition

- **WHEN** a persisted link declares a parameter, template, script, or dependency reference
- **THEN** LunaPack rejects the project configuration without changing files or lock state

#### Scenario: Install by copying content

- **WHEN** LunaPack installs a valid link
- **THEN** it writes managed file content to the workspace and does not create a filesystem symbolic link
