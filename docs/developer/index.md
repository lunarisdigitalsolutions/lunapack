# What is LunaPack?

LunaPack is a command-line tool and manifest format for reusable, versioned
engineering packs. Its `luna` CLI applies and maintains selected files without
copying an entire repository and losing ownership history.

## What you can do

- Start a project with a pack from a local folder or Git repository.
- Preview installation before files or project state change.
- Check available updates and apply them deliberately.
- Manage selected files from repositories without LunaPack manifests.
- Create a pack once and make it available to other projects.

## Start in five minutes

1. [Install Luna](installation.md).
2. [Add a pack source](sources.md).
3. [Discover and install a pack](install-packs.md).
4. [Update packs](update-packs.md) when a newer release is available.

To consume selected files without a published pack, follow
[Manage Luna Links](manage-links.md).

```text
pack.yml -> luna -> project files
```

A pack defines reusable content. LunaPack resolves and applies it. The project
records its requested packs and resolved state so the next change is clear.

## Choose a guide

- [CLI reference](cli/index.md) for the supported command surface and project
  documents.
- [Pack guide](packs/index.md) to create, release, and understand packs.
- [Troubleshooting](troubleshooting.md) for common failures and recovery.
- [Threat model](threat-model.md) for security boundaries and residual risks.
- [Contributor setup](contributing.md) for repository development.
