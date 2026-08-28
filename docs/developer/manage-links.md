# Manage Luna Links

Use Luna Links to copy and maintain selected files from a configured source
that does not publish LunaPack manifests.

## Install selected repository files

The public `github/awesome-copilot` repository is one example of a source that
can provide individual files without publishing a LunaPack manifest. Register
it as a GitHub source:

```powershell
luna sources add github awesome-copilot github/awesome-copilot
```

Install one exact agent file:

```powershell
luna links add agents-csharp-expert --source awesome-copilot --path agents --include CSharpExpert.agent.md --target .github/agents --install
```

This creates the Luna Link `agents-csharp-expert` and installs
`CSharpExpert.agent.md` into `.github/agents`.

Install every matching AI-team agent file:

```powershell
luna links add agents-ai-team --source awesome-copilot --path agents --include "ai-team*.agent.md" --target .github/agents --install
```

The `agents-ai-team` Luna Link installs every matching AI-team agent file into
`.github/agents`. Neither link is a published pack, and the upstream repository
does not need `pack.yml`.

## Select and map files

Repeat includes and exclusions to build one selection. Directories recurse;
glob matches combine with exact files and directories.

```powershell
luna links add engineering-guidance `
  --source local-guidance `
  --path repository-content/prompts `
  --include instructions `
  --include "instructions/review/**/*.prompt.md" `
  --exclude "**/experimental/**" `
  --strip-prefix instructions `
  --target .github `
  --install
```

Use `--flatten` only when selected file names are unique. Luna rejects empty
selections, prefix mismatches, path traversal, duplicate targets, and targets
owned by another pack or link before writing files.

## Inspect and update

```powershell
luna links list
luna links show agents-csharp-expert
luna outdated
luna update agents-csharp-expert
luna audit
```

Use `--remap-directory <source>=<target>` or
`--remap-file <source>=<target>` with `luna install` to override declared link
targets for that installation. Add `--save-remap` to persist the mappings in
`lunapack.yml` after a successful install. Luna records both declared and
effective target paths in the lock file.

Git links lock one immutable commit per operation. A new commit alone is not an
update when selected paths, mapped targets, definition, and content are
equivalent. Local links use content digests, not timestamps, as final evidence.

## Remove or recover

Use uninstall when a link is installed:

```powershell
luna uninstall agents-csharp-expert
```

Uninstall removes unchanged targets and lock ownership while keeping the link
definition available for later installation. If a target changed locally, Luna
preserves all state. Review or restore that file, run `luna audit`, then retry.

To stop managing a link while preserving modified targets:

```powershell
luna links rm agents-csharp-expert --force
```

Forced removal deletes unchanged targets and reports each modified target left
as unmanaged project content, then removes the link definition. A locked
source-identity failure means the configured source changed after installation;
restore the original source configuration before updating or uninstalling.

See the [Luna Links reference](cli/links.md) for complete configuration and
cache contracts.
