# Add a pack source

A source is a local directory, Git repository, or OCI registry that contains
versioned packs. Add one before discovering or installing a pack.

## Add a local source

Use a project-relative directory that contains pack folders:

```powershell
luna init
luna sources add local engineering ./engineering-packs
```

## Add a Git source

Git must be available on the process path. Register a repository URL or an
absolute local filesystem repository path, then optionally select a ref and a
repository-relative pack directory:

```powershell
luna sources add git engineering https://github.com/lunarisdigitalsolutions/lunapack.git --ref main --path projects/packs
luna sources add github engineering lunarisdigitalsolutions/lunapack --ref main --path projects/packs
```

`luna sources add github` requires `--ref`. Luna resolves a short branch or
tag name to its complete form with `git ls-remote` and rejects the source if
the name matches more than one ref. Registering the same repository again
under a different URL form, casing, or name fails with a "duplicates source"
error: Luna canonicalizes every source's URL, ref, and path and allows only
one configured source per canonical identity.

## Add an OCI registry source

Register an OCI Distribution registry without connecting to it:

```powershell
luna sources add oci private https://registry.example.com --path engineering/packs
```

The registry URL must use HTTPS and cannot contain credentials, a query, or a
fragment. The optional `--path` is an OCI repository prefix, not a filesystem
path. Luna appends each pack ID to that prefix. An empty path uses the pack ID
as the repository. A safe repository prefix embedded in the URL is accepted; if you also provide
`--path`, it must match the embedded prefix. OCI repository components must be
lowercase and use valid OCI characters.

OCI sources persist as entries in the ordered `sources` YAML sequence. Their
identity is the normalized registry origin and repository prefix, so one
registry can be configured at different prefixes. The optional `--username`
stores a non-secret username; passwords and tokens never belong in
`lunapack.yml`. See
[OCI registry credentials](cli/environment.md#oci-registry-credentials) for
noninteractive and interactive authentication.

OCI sources provide pack releases, not arbitrary files for Luna Links. Links
continue to select regular files from configured local or Git sources.

## Ignore a source locally

Use `luna sources ignore <name>` to hide one configured source for the current
user and project, or `luna sources unignore <name>` to restore access. Ignore
state lives in user settings, not `lunapack.yml` or the lock file. `luna sources
list` continues to show ignored entries as `ignored locally`. Ignoring a source
does not revoke trust, and ignored sources are not contacted for discovery,
resolution, credentials, or prompts.

Run `luna sources list` to inspect configured sources. LunaPack records source
configuration in `lunapack.yml` and the resolved Git commit in `lunapack-lock.yml`.
Names are unique project identifiers used by trust commands. Ordinary updates
remain on each lock record's configured source; an explicit version that exists
only in another configured source requires confirmation before switching.

When the same pack ID and version are available from multiple configured
sources, Luna selects the earliest source listed in `lunapack.yml`. Install and
update output, including dry runs, identifies that choice as `Selected source`
followed by the source name and type. Luna omits this line when the selected
release is available from only one source.

Packs can also propose Git sources for selected external content during install
or update. Follow [Approve pack-defined external sources](advanced/approve-external-sources.md)
before accepting those additions.

For cache locations, ref queries, temporary checkouts, and authentication
boundaries, see [Understand Git source behavior](advanced/git-source-behavior.md).

## OCI registry behavior

Luna validates the registry manifest, config, ZIP layer, and SHA-256 digests
before a release can enter the normal catalog or lifecycle. OCI releases use
the same install, update, inspect, trust, and ownership rules as local and Git
packs. Lock entries retain the registry, repository, tag, manifest digest, and
content digest. A moved tag cannot replace the bytes already recorded by an
installed lock.

Registry-wide discovery depends on the registry granting repository
enumeration. ACR, GHCR, Harbor, and repository-scoped deployments may restrict
catalog access. Luna reports unavailable sources and continues browsing
readable sources; direct references and locked releases query the known
repository without requiring a global catalog. If all active sources fail,
discovery exits nonzero rather than reporting an empty catalog as complete.

For authenticated access, OCI bearer challenges must use an HTTPS token realm
on the configured registry origin. Luna rejects cross-origin realms before
forwarding credentials.

| Registry                                    | Authentication                                                   | Discovery notes                                                                                |
| ------------------------------------------- | ---------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| GHCR                                        | Source-bound username/token, including `GITHUB_TOKEN` in Actions | Catalog enumeration depends on package permissions; direct repository lookup can still work.   |
| Azure Container Registry                    | Source-bound credentials or optional Azure Identity              | `AcrPull`/`AcrPush` permissions and registry catalog policy determine available operations.    |
| Harbor or another OCI Distribution registry | Source-bound credentials or an operating-system credential store | Catalog enumeration depends on server permissions; direct repository lookup remains available. |

Registry tags are not a portable atomic create-if-absent mechanism. Luna refuses
to overwrite an existing version, but registry-side immutable-tag policy is
needed to protect against concurrent publishers or later tag movement.

## Rename a source

Change a configured source's name without losing installed pack or trust
history:

```powershell
luna sources rename engineering acme-engineering
```

Luna updates the configuration key together with every trust and lock-file
reference in one step, so installed packs keep resolving under the new name
without any other change.

## Remove a source

Remove source configuration by its case-sensitive name:

```powershell
luna sources rm engineering
```

`luna sources remove` is accepted as an alias of `rm`. Removal fails while an
installed pack, or its external content, still depends on that source name;
uninstall or move those packs first. Once no pack depends on the source, Luna
revokes project source and pack trust bound to that name. Installed pack
records, immutable lock evidence, and managed files remain so `luna audit`
and safe uninstallation still work. Re-add the original source or explicitly
confirm a source switch before updating those packs.
