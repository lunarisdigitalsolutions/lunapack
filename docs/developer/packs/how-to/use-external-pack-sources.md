# Use external Git files in a pack

A pack-defined source lets managed selectors retrieve files from a separate Git
repository. Consumers see and approve that dependency before LunaPack writes
project state.

## Declare a source alias

Run from the pack directory. A ref is required:

```powershell
luna pack add source github lunapack `
  lunarisdigitalsolutions/lunapack `
  --ref main `
  --description "LunaPack repository content"
```

Use `source git` for a full HTTPS, SSH, or scp-style Git URL. Add `--path` to
make selectors relative to a repository subdirectory:

```powershell
luna pack add source github lunapack-docs `
  lunarisdigitalsolutions/lunapack `
  --ref main `
  --path docs/developer
```

LunaPack resolves a branch or tag to its canonical ref when adding the source.
Do not put credentials, environment substitutions, or secrets in source URLs.

## Select remote content

Reference the alias with `--source`. File, directory, and glob selectors are
supported:

```powershell
luna pack add file README.md `
  --source lunapack `
  --target docs/lunapack-readme.md
luna pack add directory packs `
  --source lunapack-docs `
  --target docs/packs `
  --exclude '**/internal/**'
```

External selector paths are relative to the source's optional base path. They
cannot be rooted or escape with `..`. Directory and glob selectors also support
repeatable `--exclude` and `--flatten`.

Inspect declarations and verify remote files are reachable:

```powershell
luna pack sources
luna pack list
luna pack validate
```

## Test consumer approval

From an initialized fixture project, validate and preview the exact release:

```powershell
luna validate engineering-docs@1.0.0
luna install engineering-docs@1.0.0 --dry-run
luna install engineering-docs@1.0.0
```

`luna validate` selects releases from configured local catalog sources. It
checks the selected manifest and local pack content; `luna pack validate` is
the author-time command that also verifies external Git content is reachable.

Interactive install groups missing sources into an approval request that
defaults to no. `--accept-sources` allows conflict-free non-interactive
approval, but does not bypass Git authentication, path checks, script trust, or
rollback. If the same source fingerprint is already configured under another
name, LunaPack reuses the configured source.

Prefer immutable tags or commit refs for reproducible releases. A branch ref is
useful when consumers should receive upstream changes during pack updates.
