# Command reference

`luna` manages versioned packs in the selected workspace. Without
`--workspace`, the workspace is the process working directory. Use
`--workspace <directory>` or `-w <directory>` with any command to select a
different directory; relative paths resolve from the process working directory.

## Global Options

| Option                          | Default           | Behavior                                                   |
| ------------------------------- | ----------------- | ---------------------------------------------------------- |
| `--workspace <directory>`, `-w` | Current directory | Selects the project directory.                             |
| `--log-level <level>`, `-ll`    | `info`            | Accepts `verbose`, `debug`, `info`, `warning`, or `error`. |
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
  or `-r` selects a branch or commit; `--path` or `-p` limits discovery to a
  repository-relative directory.
- `luna sources add github <name> <organization/repository>`: Registers a GitHub
  repository as a Git pack source. LunaPack stores its HTTPS Git URL; `--ref`
  (`-r`) and `--path` (`-p`) match `git`.
- `luna sources list`: Lists configured local and Git sources.
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

## Catalog

- `luna discover`: Lists the latest available release of each pack.
- `luna search <query>`: Lists matching packages and their latest releases.
- `luna validate <pack-reference>`: Validates the selected release from configured
  local sources, or the latest local release when version is omitted.
- `luna inspect <pack-reference>`: Shows the selected pack's identity,
  description, license, author, parameters, and referenced packs.

## Pack Lifecycle

- `luna install <pack-reference> [<pack-reference>...]`: Resolves and installs
  one or more pack releases.
- `luna uninstall <pack-reference> [<pack-reference>...]`: Removes one or more
  installed roots and unchanged files no longer owned by another pack. When
  supplied, each version must match the currently installed release.
- `luna outdated`: Lists installed roots that have a newer available release.
- `luna update [<pack-reference>...]`: Updates all roots or one or more selected
  roots.
- `luna mv <source> <target>`: Moves one uniquely owned managed file and updates
  lock ownership. If the file was moved manually, it can rebind ownership when
  only the target exists.
- `luna audit`: Reports resolved packs, formatted source locations, dependencies,
  and managed-file ownership.

`<pack-reference>` is a pack ID or `<id>@<version>`. When version is omitted,
commands select the latest available release. `install` accepts `--dry-run`
(`-D`), `--destination` (`-d`),
`--adopt-existing` (`-a`), repeatable `--parameter` (`-p`),
`--no-variables` (`-nv`), and repeatable `--skip-variable` (`-sv`).
`update` accepts `--dry-run` (`-D`); update-all also accepts `--prompt` (`-p`).
Both `install` and `update` accept `--scripts <prompt|run|skip>`; `prompt` is
the default and requires effective trust or interactive consent for each hook.
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
