# Add a pack source

A source is a local directory or Git repository that contains versioned packs.
Add one before discovering or installing a pack.

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

Run `luna sources list` to inspect configured sources. LunaPack records source
configuration in `lunapack.yml` and the resolved Git commit in `lunapack-lock.yml`.
Names are unique project identifiers used by trust commands. Ordinary updates
remain on each lock record's configured source; an explicit version that exists
only in another configured source requires confirmation before switching.

Packs may require external Git content under their own local aliases. During
install or update, Luna reuses an equivalent configured fingerprint without a
prompt. Missing sources appear together in one sanitized approval request that
defaults to no. Use `--accept-sources` only for conflict-free non-interactive
approval; it does not bypass Git authentication, path checks, script trust, or
transaction rollback. A dry run reports mappings, proposed additions, approval
requirements, and file actions without prompting or writing state.

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
