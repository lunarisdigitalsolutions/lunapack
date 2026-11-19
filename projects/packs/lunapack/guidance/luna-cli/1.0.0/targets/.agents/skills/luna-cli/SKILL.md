---
name: luna-cli
description: Use when a user asks to discover, evaluate, install, update, audit, author, validate, or remove LunaPack packs with the Luna CLI. Provides safe, harness-independent workflows with explicit approval boundaries for project mutations, sources, trust, and lifecycle scripts.
license: MIT
metadata:
  author: Lunaris Digital Solutions
  version: "1.0.0"
---

# Luna CLI

Use the installed `luna` help as the command authority. This skill organizes
safe workflows around that command surface; it does not define a second CLI
contract.

## Approval Boundaries

- Treat discovery, inspection, validation, search, pack display, source
  listing, trust listing, outdated checks, and audits as read-only.
- Obtain explicit approval before changing project files, project
  configuration, installed packs, target locations, or pack authoring files.
- A request for one mutation authorizes only that exact operation. It does not
  authorize additional source additions, persistent trust changes, remaps,
  parameters, or lifecycle-script execution.
- Review proposed external sources before accepting them. Never add
  `--accept-sources` without explicit approval of the named source additions.
- Never persist trust or use `--scripts run` without explicit approval of the
  trust scope or exact reviewed scripts. Prefer `--scripts skip` while
  evaluating and applying routine changes.
- Before an install or update that lacks prior authorization, preview the
  exact release with `--dry-run --scripts skip` and ask before running the
  mutation.

## Establish The Workspace

Confirm the CLI is available and inspect only configured state:

```powershell
luna --version
luna --help
luna sources list
```

Run commands in the intended project root. When another workspace is
intended, select it explicitly with `--workspace <directory>` and keep
all discovery, preview, and mutation commands on that same workspace.

## Evaluate A Pack

Use a broad read-only workflow before installation:

```powershell
luna discover
luna search <term>
luna inspect <pack-id>@<version>
luna validate <pack-id>@<version>
```

Use the exact version in the `inspect`, `validate`, and install commands.
Review source identity, author, license, description, managed targets,
parameters, dependencies, external sources, and lifecycle hooks. A pack ID
alone does not establish publisher identity.

When installation is intended, preview it with scripts disabled:

```powershell
luna install <pack-id>@<version> --dry-run --scripts skip
```

Review the selected source, managed-file actions, proposed source additions,
parameters, remaps, and lifecycle actions. A successful dry run does not
authorize a later write or prove that a script is safe.

After explicit approval of the exact operation, install with scripts skipped:

```powershell
luna install <pack-id>@<version> --scripts skip
```

Use an accepted `--parameter <name>=<value>` only when the user selected that
value. Review any proposed external source separately. Run `luna audit` after
installation and verify the requested targets and version.

## Configure Sources

Adding or changing a source mutates `lunapack.yml` and may affect later
resolution. Obtain explicit approval before using a source-add command:

```powershell
luna sources add local <name> ./relative-pack-catalog
luna sources add github <name> <owner/repository> `
  --ref <branch-tag-or-commit> `
  --path <repository-relative-pack-root>
```

List configured identities afterward:

```powershell
luna sources list
```

Prefer an exact ref when reproducibility matters. Do not assume a pack's
identity verifies its repository, and do not grant source or pack trust merely
because a source was approved for discovery.

## Author Or Edit A Pack

Work from the pack release directory. Use `luna pack` commands rather than
building an invalid manifest incrementally by hand:

```powershell
luna pack init `
  --id example-documentation-standard `
  --author "Example Engineering" `
  --license MIT
luna pack add file targets/docs/standard.md --target docs/standard.md
luna pack add directory targets/config --target config
luna pack set description "Describe the pack's installed behavior."
luna pack list
luna pack validate
```

Keep synthetic authoring IDs prefixed with `example-`. Use supported
`luna pack add`, `luna pack set`, and `luna pack rm` subcommands for managed
files, hooks, tags, parameters, sources, and references. Preserve the existing
manifest when a command fails; report the validation error instead of
rewriting away the failure.

For a release-ready result, validate the local release and then register its
catalog as a project source before running catalog validation:

```powershell
luna pack validate
luna validate <pack-id>@<version>
```

Do not publish a pack, add a source, grant trust, or run lifecycle scripts
unless the user approved those actions separately.

## Maintain Installed Packs

Match the command to the requested outcome. Read-only inspection does not
authorize an update or uninstall:

```powershell
luna outdated
luna outdated --offline
luna audit
```

Preview an update with scripts disabled and require explicit approval before
applying it:

```powershell
luna update <pack-id> --dry-run --scripts skip
luna update <pack-id>@<version> --scripts skip
```

Review local modifications and target ownership before uninstall. Luna
uninstall has no dry-run option. After explicit approval, remove the named
root:

```powershell
luna uninstall <pack-id> --scripts skip
```

Do not edit `lunapack-lock.yml` by hand to manufacture ownership or recovery
state.

## Report The Result

Report the exact pack identity and version, selected source identity, target
effects, whether configuration changed, and whether hooks were skipped or
explicitly approved. When a command fails, preserve its diagnostic, exit
status, and current files; explain the next safe action without claiming that
the requested mutation succeeded.
