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

Git must be available on the process path. Register the repository, then
optionally select a ref and a repository-relative pack directory:

```powershell
luna sources add git engineering https://github.com/acme/engineering-packs
luna sources add git engineering https://github.com/acme/engineering-packs --ref main --path packs
luna sources add github engineering acme/engineering-packs
```

Run `luna sources list` to inspect configured sources. LunaPack records source
configuration in `lunapack.yml` and the resolved Git commit in `lunapack-lock.yml`.
Names are unique project identifiers used by trust commands. Ordinary updates
remain on each lock record's configured source; an explicit version that exists
only in another configured source requires confirmation before switching.

## Remove a source

Remove source configuration by its case-sensitive name:

```powershell
luna sources remove engineering
```

Luna also revokes project source and pack trust bound to that name. Installed
pack records, immutable lock evidence, and managed files remain so `luna audit`
and safe uninstallation still work. Re-add the original source or explicitly
confirm a source switch before updating those packs.
