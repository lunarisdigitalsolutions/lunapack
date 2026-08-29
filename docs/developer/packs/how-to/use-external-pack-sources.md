# Use external Git files in a pack

A pack-defined source lets managed selectors retrieve files from a separate Git
repository. Consumers see and approve that dependency before LunaPack writes
project state.

Run these authoring commands against the synthetic
`example-documentation-standard` pack created by
[Create a first pack](../tutorials/first-pack.md). That tutorial also shows how
to register its containing directory as a local catalog in a fixture project.

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

A source alias becomes a runtime requirement only when at least one selector
using it survives condition evaluation. If every selector for an alias is
excluded, Luna does not resolve or validate that source, request consumer
approval, add workspace source configuration, or write external-source lock
evidence. A later parameter value that selects one of those files activates the
complete source workflow.

Inspect declarations and verify remote files are reachable:

```powershell
luna pack sources
luna pack list
luna pack validate
```

## Test consumer approval

From the initialized fixture with that local catalog configured, validate and
preview the exact release:

```powershell
luna validate example-documentation-standard@1.0.0
luna install example-documentation-standard@1.0.0 --dry-run
```

Choose interactive approval:

```powershell
luna install example-documentation-standard@1.0.0
```

Or, after reviewing the dry run, choose noninteractive approval in a fresh
fixture:

```powershell
luna install example-documentation-standard@1.0.0 --accept-sources
```

`luna validate` selects releases from configured local catalog sources. It
checks the selected manifest and local pack content; `luna pack validate` is
the author-time command that also verifies external Git content is reachable.

Interactive install groups missing sources into an approval request that
defaults to no. `--accept-sources` allows conflict-free non-interactive
approval, but does not bypass Git authentication, path checks, script trust, or
rollback. If the same source fingerprint is already configured under another
name, LunaPack reuses the configured source.

See [Understand Git source behavior](../../advanced/git-source-behavior.md) for
storage, query, cache, and authentication details.

Prefer immutable tags or commit refs for reproducible releases. A branch ref is
useful when consumers should receive upstream changes during pack updates.
