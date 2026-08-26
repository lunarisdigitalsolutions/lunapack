# CLI overview

`luna` is the LunaPack CLI. It manages versioned engineering packs in a consumer project. The MVP
supports local and Git sources, catalog discovery, installation, update,
ownership audit, and conservative removal.

Run `luna` without a subcommand to see the selected workspace's maturity and
next recommended commands. Luna also follows successful core workflow commands
and recoverable prerequisite errors with up to three contextual actions.

Published Luna binaries are self-contained Native AOT executables for Linux
and Windows x64.

## What It Preserves

`lunapack.yml` records sources and requested root packs. `lunapack-lock.yml` records
the resolved graph, immutable source evidence, managed target paths, and
rendered-content digests. Lifecycle commands update both documents together.

Use a dry run before installation or update when a project needs a plan without
file or state changes. Uninstall removes only unchanged targets that no longer
have an owner.
