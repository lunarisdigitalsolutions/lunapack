# Pack manifest reference

`pack.yml` declares a pack. It requires a hyphen-separated alphanumeric `id`,
non-empty `author` and `license` values, and a semantic `version`. Empty content
collections remain valid during incremental authoring. `name`, `description`,
`homepage`, and `tags` are optional metadata. Discovery and search exclude
manifests missing author or license attribution.

```yml
id: documentation-standard
name: Documentation standard
version: 1.0.0
license: MIT
author: Lunaris Digital Solutions <info@lunaris.digital>
description: Adds the engineering documentation standard.
tags:
  - documentation
  - engineering
managedFiles:
  - source: templates/standard.md
    target: docs/standard.md
```

| Field                  | Rules                                                                                                                                                                           |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                   | Required stable identifier: alphanumeric segments joined by single hyphens.                                                                                                     |
| `version`              | Required Semantic Version.                                                                                                                                                      |
| `name`                 | Optional non-empty human-readable name.                                                                                                                                         |
| `author`               | Required non-empty author or maintainer attribution.                                                                                                                            |
| `homepage`             | Optional absolute HTTP or HTTPS URI.                                                                                                                                            |
| `license`              | Required non-empty license identifier or expression.                                                                                                                            |
| `managedFiles`         | Each entry has one `source`, `directory`, or `glob` selector and a project-relative `target`.                                                                                   |
| `packs`                | Each composite reference has a hyphen-separated alphanumeric ID and an exact version.                                                                                           |
| `parameters`           | Identifier-named `string`, `bool`, or `enum` declarations. Enums require unique values and may set `multiple: true`; multi-select defaults are unique arrays of allowed values. |
| Reference `parameters` | String, Boolean, or unique string-array bindings for a referenced pack. Runtime validation checks arrays against the target multi-select enum.                                  |
| `condition`            | A Boolean name or negation, a scalar string or enum equality comparison, or `"literal" in identifier` membership joined with logical AND, logical OR, and parentheses.          |
| `strategy`             | `copy` with `overwrite`, `fail-if-exists`, `skip-if-exists`, or `backup-and-overwrite`; or `merge` with `lines`, `section`, or `json`.                                          |
| `tags`                 | Optional list of up to 15 unique, non-empty tags. Search matches tags; discover lists them, and inspect previews the first five.                                                |
| `template`             | Enables Scriban parsing. Defaults to `false`; set `true` only when this source uses parameters or Scriban functions.                                                            |
| `hooks`                | Ordered `script` or `instruction` declarations grouped by lifecycle event.                                                                                                      |

## Lifecycle hooks

Optional `hooks` arrays run or display ordered work around install, update, and
uninstall.
Each declaration is a `script` or `instruction`. Script hooks use either a
direct executable command or a packed file with an explicit runner. Arguments
are Scriban templates rendered from resolved pack parameters. Each rendered
item remains one literal argv value, not part of a shell command string.

```yml
hooks:
  preInstall:
    - type: instruction
      file: instructions/setup.md
      templating: true
    - type: script
      file: scripts/setup.ps1
      runner: pwsh
      arguments:
        - -ProjectType
        - '{{ projectType }}'
      description: Configure project tooling.
  postUpdate:
    - type: script
      command: dotnet
      arguments:
        - tool
        - restore
  postUninstall:
    - type: instruction
      file: instructions/cleanup.md
```

Supported hook names are `preInstall`, `postInstall`, `preUpdate`, `postUpdate`,
`preUninstall`, and `postUninstall`. Script items require `type: script`. Packed-file scripts require
both `file` and `runner`; direct scripts require `command`. Instruction items
require `type: instruction` and a pack-relative Markdown `file`; optional
`templating: true` enables Scriban before display. Hook order within each event
is significant. A composite reference can set `disabledHooks` to suppress every
typed hook in selected events for that transient pack.

```yml
id: application-foundation
version: 1.0.0
license: MIT
author: Lunaris Digital Solutions <info@lunaris.digital>
packs:
  - id: documentation-standard
    version: 1.0.0
    disabledHooks:
      - preInstall
      - postUpdate
```

Consumers select `luna install`, `luna update`, and `luna uninstall` behavior with
`--scripts prompt|run|skip`. Prompt mode requires a matching source or
source-plus-pack trust entry, or interactive approval for each untrusted hook.
Run mode permits all non-suppressed scripts for one invocation; skip mode runs
no scripts. `--skip-instructions` independently prevents instruction loading
and display. Scripts execute with the invoking user's authority, so authors
should keep them minimal and describe externally visible effects. Instructions
never execute and do not use script trust. Argument and instruction rendering
occurs before dry-run formatting; script arguments render before trust
authorization. `command`, `runner`, and `file` remain literal. See
[Use Scriban templates](../how-to/use-scriban-templates.md).

Uninstall resolves hooks from the exact releases recorded in the lock file. If
their source is unavailable, LunaPack warns and proceeds without uninstall hooks.

Manifests using the former top-level `scripts` map must migrate to `hooks`, turn
each event value into an array, and add `type: script` to every declaration.
LunaPack rejects the old shape so mixed ordering cannot be ambiguous.

CLI authoring migration is direct:

- Replace `luna pack add script ...` with `luna pack add hook script ...`.
- Replace `luna pack rm script <event>` with `luna pack rm hook <event>
<position>`.
- Replace `luna pack scripts` with `luna pack hooks`.

The manifest directory is the pack root. Source selectors read from that root;
directory and glob matches retain their relative paths below the target.

Managed-file `target` values are portable pack defaults. Consumers can remap
them in project-level `lunapack.yml` configuration or through `luna install`
options; pack authors do not declare consumer remapping in `pack.yml`. See
[Remap managed targets](../../remap-targets.md) for
mapping syntax, precedence, lifecycle retention, and explicit relocation.

Selected source files copy as-is unless their selector sets `template: true`.
Template-enabled files are UTF-8 Scriban templates. Resolved parameter names are
available as template globals, and supported date-time functions are available.
Templates cannot access the filesystem, host services, or custom functions.
