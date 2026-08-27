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

| Field                  | Rules                                                                                                                                  |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                   | Required stable identifier: alphanumeric segments joined by single hyphens.                                                            |
| `version`              | Required Semantic Version.                                                                                                             |
| `name`                 | Optional non-empty human-readable name.                                                                                                |
| `author`               | Required non-empty author or maintainer attribution.                                                                                   |
| `homepage`             | Optional absolute HTTP or HTTPS URI.                                                                                                   |
| `license`              | Required non-empty license identifier or expression.                                                                                   |
| `managedFiles`         | Each entry has one `source`, `directory`, or `glob` selector and a project-relative `target`.                                          |
| `packs`                | Each composite reference has a hyphen-separated alphanumeric ID and an exact version.                                                  |
| `parameters`           | Identifier-named `string`, `bool`, or `enum` declarations. Optional display metadata labels prompts; enums require unique values.      |
| Reference `parameters` | String or Boolean bindings for a referenced pack.                                                                                      |
| `condition`            | A Boolean name or negation, or a quoted string or enum equality comparison joined with logical AND, logical OR, and parentheses.       |
| `strategy`             | `copy` with `overwrite`, `fail-if-exists`, `skip-if-exists`, or `backup-and-overwrite`; or `merge` with `lines`, `section`, or `json`. |
| `tags`                 | Optional list of up to 15 unique, non-empty tags. Search matches tags; discover lists them, and inspect previews the first five.       |
| `template`             | Enables Scriban parsing. Defaults to `false`; set `true` only when this source uses parameters or Scriban functions.                   |

## Lifecycle scripts

Optional `scripts` hooks run around install and update. Each hook is either a
direct executable command or a packed file with an explicit runner. Arguments
are Scriban templates rendered from resolved pack parameters. Each rendered
item remains one literal argv value, not part of a shell command string.

```yml
scripts:
  preInstall:
    file: scripts/setup.ps1
    runner: pwsh
    arguments:
      - -ProjectType
      - '{{ projectType }}'
    description: Configure project tooling.
  postUpdate:
    command: dotnet
    arguments:
      - tool
      - restore
```

Supported hook names are `preInstall`, `postInstall`, `preUpdate`, and
`postUpdate`. Packed-file hooks require both `file` and `runner`; direct hooks
require `command`. `file` must remain pack-relative. A composite reference can
set `disabledHooks` to suppress selected hook types for that transient pack.

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

Consumers select `luna install` and `luna update` behavior with
`--scripts prompt|run|skip`. Prompt mode requires a matching source or
source-plus-pack trust entry, or interactive approval for each untrusted hook.
Run mode permits all non-suppressed hooks for one invocation; skip mode runs no
hooks. Hooks execute with the invoking user's authority, so authors should keep
them minimal and describe externally visible effects. Argument rendering occurs
before dry-run formatting and trust authorization. `command`, `runner`, and
`file` remain literal. See [Use Scriban templates](../how-to/use-scriban-templates.md).

The manifest directory is the pack root. Source selectors read from that root;
directory and glob matches retain their relative paths below the target.

Managed-file `target` values are portable pack defaults. Consumers can remap
them in project-level `lunapack.yml` configuration or through `luna install`
options; pack authors do not declare consumer remapping in `pack.yml`. See
[Discover and install packs](../../install-packs.md#remap-managed-targets) for
mapping syntax, precedence, lifecycle retention, and explicit relocation.

Selected source files copy as-is unless their selector sets `template: true`.
Template-enabled files are UTF-8 Scriban templates. Resolved parameter names are
available as template globals, and supported date-time functions are available.
Templates cannot access the filesystem, host services, or custom functions.
