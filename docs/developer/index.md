# What is LunaPack?

LunaPack is a command-line tool and manifest format for reusable, versioned
engineering packs. Its `luna` CLI applies and maintains selected files without
copying an entire repository and losing ownership history.

## What you can do

- Start a project with a pack from a local folder or Git repository.
- Preview installation before files or project state change.
- Check available updates and apply them deliberately.
- Get selected files from Git without a pack.
- Create a pack once and make it available to other projects.

## Get started

1. [Install Luna](installation.md).
2. [Add a pack source](sources.md).
3. [Evaluate a pack](evaluate-packs.md) before changing project files.
4. [Discover and install a pack](install-packs.md).
5. [Update packs](update-packs.md) when a newer release is available.

```text
pack.yml -> luna -> project files
```

A pack defines reusable content. LunaPack resolves and applies it. The project
records its requested packs and resolved state so the next change is clear.

## Choose a guide

- Everyday work covers [updates](update-packs.md),
  [parameters and variables](parameters-and-variables.md),
  [target remapping](remap-targets.md), and
  [lifecycle hooks](lifecycle-hooks.md).
- Advanced workflows cover [Luna Links](manage-links.md),
  [external-source approval](advanced/approve-external-sources.md),
  [Git source behavior](advanced/git-source-behavior.md),
  [existing-file adoption](advanced/adopt-existing-files.md), and
  [audit and recovery](advanced/audit-and-recover.md).
- [CLI reference](cli/index.md) for the supported command surface and project
  documents.
- [Pack guide](packs/index.md) to create, release, and understand packs.
- [Automation guide](automation.md) for noninteractive validation and installs.
- [Troubleshooting](troubleshooting.md) for common failures and recovery.
- [Usage threat model](threat-model.md) for pack, source, workspace, and lifecycle
  security boundaries.
