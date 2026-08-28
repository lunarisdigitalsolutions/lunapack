# Command reference

`luna` manages versioned packs in the selected workspace. Without
`--workspace`, the workspace is the process working directory. Use
`--workspace <directory>` or `-w <directory>` with any command to select a
different directory; relative paths resolve from the process working directory.
Running `luna` without a subcommand first displays root command help, then
summarizes configured sources and installed root packs and recommends the next
commands for the current workspace stage.

## Global Options

| Option                          | Default           | Behavior                                                   |
| ------------------------------- | ----------------- | ---------------------------------------------------------- |
| `--workspace <directory>`, `-w` | Current directory | Selects the project directory.                             |
| `--log-level <level>`, `-ll`    | `info`            | Accepts `verbose`, `debug`, `info`, `warning`, or `error`. |
| `--suppress-next-steps`         | `false`           | Suppresses contextual next-step recommendations.           |
| `--help`, `-h`, `-?`            | Not applicable    | Shows command help and returns success.                    |
| `--version`                     | Not applicable    | Shows the Luna version and returns success.                |

## Project And Sources

- `luna init`: Creates version-1 `lunapack.yml` and `lunapack-lock.yml`.
- `luna variables list`: Lists configured project variables in a table.
- `luna variables set <name> <value>`: Sets a string project variable. Names
  must start with a letter or underscore and may contain letters, digits, and
  underscores.
- `luna variables rm <name>`: Removes a configured project variable.
- `luna remap list`: Lists global managed-file target remappings.
- `luna remap set <directory|file> <target> <new-target>`: Creates or replaces
  one global managed-file target remapping.
- `luna remap rm <directory|file> <target>`: Removes one global managed-file
  target remapping.
- `luna sources add local <name> <path>`: Registers an existing project-relative
  directory of packs.
- `luna sources add git <name> <repository-url>`: Registers a Git pack source. `--ref`
  or `-r` selects a branch or commit and resolves a short branch or tag name to
  its complete ref through `git ls-remote`; `--path` or `-p` limits discovery to
  a repository-relative directory. Rejects a repository URL that canonicalizes
  to an already-configured source.
- `luna sources add github <name> <organization/repository>`: Registers a GitHub
  repository as a Git pack source. LunaPack stores its HTTPS Git URL; `--ref`
  (`-r`) is required and resolved the same way as `git`; `--path` (`-p`)
  matches `git`.
- `luna sources list`: Lists configured local and Git sources.
- `luna sources rename <current-id> <new-id>`: Renames a configured source,
  atomically updating trust and lock-file references bound to its previous
  name.
- `luna sources rm <name>` (alias `remove`): Removes one configured source and
  project trust bound to its name. Refuses to remove a source while an
  installed pack, or its external content, still depends on that source name.
  Installed pack records, lock provenance, and managed files remain.
- `luna trust source <name>...`: Grants lifecycle-script trust to configured
  sources.
- `luna trust pack <id>... --source <name>`: Grants trust only to selected pack
  IDs from one configured source.
- `luna trust list`: Lists persisted trust.
- `luna trust revoke source <name>...`: Revokes source trust.
- `luna trust revoke pack <id>... --source <name>`: Revokes pack trust from one
  configured source.

Trust commands accept mutually exclusive `--project` and `--global` scopes.
`--project` writes portable project configuration; `--global` applies to the
current user across projects. Omitting both uses local-user settings for this
project. Pack trust and pack-trust revocation require `--source` or `-s`. See
[Scripts and trust](trust-and-scripts.md) before granting trust.

## Luna Links

- `luna links add <name> --source <name> --include <selector>`: Adds a
  project-owned source selection. Repeat `--include` (`-i`) and `--exclude`
  (`-e`); use `--path`, `--target` (`-t`), `--ref`, `--strip-prefix`,
  `--flatten`, `--install`, or `--force` as needed.
- `luna links list`: Lists configured links and installation status.
- `luna links show <name>`: Shows selectors, resolved Git evidence, selected
  file count, and local modification count.
- `luna links rm <name>`: Removes an uninstalled definition.
- `luna links rm <name> --force`: Removes definition and ownership, deletes
  unchanged targets, and preserves modified targets.

`luna install`, named `luna update`, `luna uninstall`, `luna outdated`, and
`luna audit` operate on links as well as packs. See
[Manage Luna Links](../manage-links.md) and the
[Luna Links reference](links.md).

## Catalog

- `luna discover`: Lists the latest available release of each pack.
- `luna search <query>`: Lists matching packages and configured links. Package
  results include their latest releases; link results include source, target,
  and installation status.
- `luna validate <pack-reference>`: Validates the selected release from configured
  local sources, or the latest local release when version is omitted.
- `luna inspect <pack-reference>`: Shows the selected pack's identity,
  description, license, author, parameters, and referenced packs.

## Pack Authoring

- `luna pack`: Recommends `luna pack init` when the current directory has no
  `pack.yml`; otherwise recommends viewing, editing, and validating the local
  manifest.
- `luna pack init --id <id> --author <author> --license <license> [--version <version>]`:
  Creates local `pack.yml`; version defaults to `1.0.0`. Missing required
  values prompt only in an interactive terminal. The license prompt defaults
  to `MIT`, so Enter accepts it. Invalid prompted pack IDs display their error
  immediately and prompt again before collecting remaining values.
- `luna pack add file|directory|glob <path>`: Adds managed content.
  `--source`, repeatable `--exclude`, `--flatten`, `--target`,
  `--strategy <type>:<method>`, `--template`, and `--condition` configure the
  selector. Globs require a target when none can be inferred.
- `luna pack add hook script command <event> <command> [<arguments>...]`: Appends
  a direct executable hook. Use `--description` to explain its purpose.
- `luna pack add hook script file <event> <file> <runner> [<arguments>...]`:
  Appends a packed-file executable hook. Both script forms accept
  `--description` (`-d`).
- `luna pack add hook instruction <event> <file>`: Appends a Markdown
  instruction hook. Use `--templating` to render it with Scriban. Every hook
  add command accepts `--replace <position>` to replace one existing hook at
  its one-based event position.
- `luna pack add source git <name> <repository-url> --ref <ref>`: Adds a
  pack-local external Git alias. `--path`, `--description`, and `--manifest`
  are optional.
- `luna pack add source github <name> <owner/repository> --ref <ref>`: Adds the
  same declaration through GitHub shorthand.
- `luna pack sources`: Lists sanitized source identities, canonical refs, base
  paths, and managed-selector reference counts.
- `luna pack add reference <id> <version>`: Adds an exact composite reference.
  Repeat `--parameter <name>=<value>` and `--disable-hook <hook>` as needed;
  `--replace` updates an existing ID.
- `luna pack add tag <value>`: Adds one unique tag.
- `luna pack set <property> <value>`: Sets `id`, `name`, `version`,
  `description`, `author`, `homepage`, or `license`.
- `luna pack set parameter <name> <string|bool|enum>`: Creates or replaces a
  parameter. Use `--required`, repeatable enum `--value`, `--display-name`, and
  `--description`; `--default` supplies a typed prompt or optional binding default.
- `luna pack set reference <id> <version>`: Creates or replaces a composite
  reference.
- `luna pack rm <selector>`: Removes one exact managed selector.
- `luna pack rm hook <event> <position>`: Removes one hook at its one-based
  event position.
- `luna pack rm source <name>`: Removes an unreferenced source alias.
- `luna pack rm reference|parameter|metadata <name>` and `luna pack rm tag
<value>`: Remove named declarations. ID and version cannot be removed.
- `luna pack list`, `luna pack hooks`, and `luna pack show`: Display local
  manifest contents. Hook output preserves event and declaration order.
- `luna pack validate`: Validates local and external selector reachability,
  warns about unused source aliases, and never executes hooks or changes
  trust.

Every mutation validates the complete candidate and atomically replaces
`pack.yml`. Failure preserves the previous file. File, directory, target, and
hook-file input accepts either separator, rejects rooted or escaping paths,
and persists with `/`. Every pack manifest requires a non-empty `author` and
`license`; packs missing either value are excluded from discovery and search.
Pack and composite-reference IDs use alphanumeric segments separated by single
hyphens. Successful pack commands print contextual next actions, such as
adding content, viewing the manifest, or validating it.

## Pack Lifecycle

- `luna install <pack-reference> [<pack-reference>...]`: Resolves and installs
  one or more pack releases.
- `luna uninstall <pack-reference> [<pack-reference>...]`: Removes one or more
  installed roots and unchanged files no longer owned by another pack. When
  supplied, each version must match the currently installed release.
- `luna outdated`: Lists installed roots with a newer release or changed
  external content. `--offline` avoids remote checks and reports uncertainty.
- `luna update [<pack-reference>...]`: Updates all roots or one or more selected
  roots.
- `luna mv <source> <target>`: Moves one managed file or all managed files below
  a directory and updates lock ownership. If files were moved manually, it can
  rebind ownership when only the targets exist. `--save-remap` also records the
  move as a reusable project mapping.
- `luna audit`: Reports resolved packs, dependencies, external alias mappings,
  fingerprints, refs, commits, source and target paths, ownership, digests, and
  drift or local-modification status.

`<pack-reference>` is a pack ID or `<id>@<version>`. When version is omitted,
commands select the latest available release. `install` accepts `--dry-run`
(`-D`), `--destination` (`-d`),
`--adopt-existing` (`-a`), repeatable `--parameter` (`-p`),
`--no-variables` (`-nv`), and repeatable `--skip-variable` (`-sv`).
Install also accepts repeatable `--remap-directory <source>=<target>` and
`--remap-file <source>=<target>` options. Add `--save-remap` to persist those
mappings after a successful installation. `--save-remap` requires at least one
remapping option. Use `@ignore` as a target to omit a matching file or directory
tree from installation and lock ownership. Updates preserve newly ignored
local files without updating them; removing the mapping allows omitted files to
be installed by a later update.
`update` accepts `--dry-run` (`-D`); update-all also accepts `--prompt` (`-p`).
Both install and update accept `--accept-sources` for conflict-free proposed
source additions. `install`, `update`, and `uninstall` accept
`--scripts <prompt|run|skip>`; `prompt` is the default and requires effective
trust or interactive consent for each script hook. Interactive consent defaults
to no. Use `--skip-instructions` to suppress instruction loading and display
without changing script consent behavior. Uninstall also accepts lifecycle
`--parameter`, `--no-variables`, and `--skip-variable` inputs.
Interactive sessions show one prepared instruction step at a time and wait for
Enter. Noninteractive sessions print all instruction content without reading
input. Dry runs report validated instruction metadata and step counts without
entering guided display.
Instruction display omits the document H1 and emphasizes headings, code, links,
bold text, and italic text when ANSI styling is available.
When more than one reference is supplied, lifecycle commands process them in
the order given. Install reuses already locked transient packs at the same
version and reports a conflict when a new root requires a different version.
An installed requested root in a multi-reference install emits a warning and is
skipped. Earlier successful references remain applied if a later reference
fails. When an explicit install version is unavailable but the pack exists,
LunaPack suggests its latest available version.
When a required install parameter has no command-line, composite-pack, or
project-variable value, LunaPack prompts for it. Prompts use the manifest's
display name and description when available.

`discover --versions <count>` and `search --versions <count>` (or `-v`) list
up to that many distinct releases for each package. Both commands show the
latest release by default, use separate Pack and Version columns, and order
requested releases by descending Semantic Version. The count must be from one
through 10.

LunaPack writes output through Spectre.Console on standard output; info output
is plain, while
verbose, debug, warning, and error output has colored level prefixes. The default
level is `info`; longer catalog and lifecycle operations show a spinner. Discover,
search, audit, outdated, and variable-list results render as tables.
Successful actions are green; guidance and instruction headings use cyan.
Catalog summaries include elapsed duration. Managed-file phases and successful
scripts report their own execution duration; time spent waiting for user input
is excluded. Install and update success lines include the selected pack version.

Successful initialization, source changes, catalog exploration, installation,
updates, and uninstallation append a bounded recommendation block when a useful
next action exists. Dry runs do not claim that workspace state advanced. Missing
workspace or source prerequisites and unresolved pack references include
recovery commands while retaining a nonzero exit code. Use
`--suppress-next-steps` with any command to omit recommendation and recovery
guidance.

Successful commands return exit code `0`. Invalid input, validation failures,
resolution conflicts, denied trust, Git failures, and filesystem or state-write
failures return a nonzero exit code. Luna does not currently provide JSON output
or stable machine-readable diagnostic codes.

System.CommandLine supplies completion for commands and options. LunaPack adds
the lower-case `verbose`, `debug`, `info`, `warning`, and `error` suggestions
for `--log-level` and `-ll`.

Catalog commands ignore invalid packs in an otherwise reachable source. Run
`validate` for a pack to see its manifest and selected-source-file issues; use
the `debug` log level to inspect ignored catalog candidates.

For common failures and recovery steps, see [Troubleshooting](../troubleshooting.md).
