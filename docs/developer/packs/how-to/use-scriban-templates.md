# Use Scriban templates

Use Scriban when a managed file or lifecycle script argument must include a
value chosen by the pack consumer. Keep static content as a normal managed file.

Run these authoring examples against the synthetic
`example-documentation-standard` pack created by
[Create a first pack](../tutorials/first-pack.md).

## Declare parameters

Declare each input in `pack.yml`. Names become Scriban globals.

```yml
parameters:
  companyName:
    type: string
    required: true
    displayName: Company name
    description: Legal entity written into generated files.
  includeWorkflow:
    type: bool
  projectType:
    type: enum
    default: service
    values:
      - library
      - service
  features:
    type: enum
    multiple: true
    default: [api]
    values:
      - api
      - docker
```

Consumers pass values with repeatable `--parameter` or `-p` options:

```powershell
luna install example-documentation-standard@1.0.0 \
  -p companyName="Lunaris Digital Solutions" \
  -p projectType=service \
  -p features=api \
  -p features=docker
```

Matching values in the project-level `variables` mapping are used when no
explicit value is supplied. Explicit command arguments take precedence.
Use `default` with a string, Boolean, allowed scalar enum value, or unique array
of allowed multi-select enum values. Optional parameters use their default
automatically. Required parameters still prompt and offer the default.

## Render managed files

Set `template: true` only for selectors whose UTF-8 content uses Scriban.

```yml
managedFiles:
  - source: targets/NOTICE.md
    target: NOTICE.md
    template: true
  - source: targets/.github/workflows/validation.yml
    target: .github/workflows/build.yml
    template: true
    condition: includeWorkflow
```

A template can use resolved parameter names and Scriban date functions:

```scriban
Copyright {{ date.now.year }} {{ companyName }}
Project type: {{ projectType }}
{{ if features contains "docker" }}Docker support enabled{{ end }}
```

Multi-select values are Scriban arrays. Use `features contains "docker"` for
membership. Empty selections behave as empty arrays.

Template parsing is strict. An unknown variable, invalid expression, or invalid
UTF-8 input fails planning before project files or state change. Templates do
not receive filesystem, host-service, or include access.

## Resolve managed-file paths

Reference another managed file by its declared `target`. LunaPack resolves the
consumer's effective target after remapping:

```scriban
See {{ files.path "docs/development/code-review.md" }}.
```

For a reference relative to the current template's effective target directory,
use `files.relative_path`:

```scriban
See {{ files.relative_path "docs/development/code-review.md" }}.
```

Both functions use the complete condition-selected installation plan. Directory
and glob selectors resolve by their expanded concrete declared targets. Returned
paths always use `/`, including on Windows. Install, update, and dry-run use the
same resolution behavior.

If the declared target is missing, excluded by a condition, or ambiguous,
LunaPack warns and returns the supplied target unchanged. This fallback does not
fail the operation. The `files` object exposes no filesystem discovery, content,
or existence checks.

These functions are available only to template-enabled managed files.
Instruction and lifecycle script argument templates continue to expose resolved
parameters and Scriban date functions, but not `files`.

## Render lifecycle hooks

Lifecycle script `arguments` are Scriban templates. Each rendered list item is
passed as one literal process argument; LunaPack does not concatenate arguments
into a shell command.

```yml
hooks:
  postInstall:
    - type: script
      command: dotnet
      arguments:
        - new
        - '{{ projectType }}'
        - --name
        - '{{ companyName }}'
```

Rendering occurs before trust authorization. Dry-run and consent output show the
exact arguments that execution will receive. `command`, `runner`, and `file`
remain literal and cannot be parameterized.

Instruction hooks render only when `templating: true` is explicit:

```yml
hooks:
  postInstall:
    - type: instruction
      file: instructions/next-steps.md
      templating: true
```

Instruction rendering uses the same resolved parameters and strict variable
rules. Static instructions leave `templating` unset. Invalid UTF-8, templates,
or unknown variables fail before project mutation.

## Escape literal delimiters

For a template-enabled file that must emit literal `{{` or `}}`, use a Scriban
string expression:

```scriban
{{ "{{ literal }}" }}
```

Prefer leaving `template` unset when the complete file is literal. This also
allows binary content to copy without UTF-8 decoding.
