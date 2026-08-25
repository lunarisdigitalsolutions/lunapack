# Use Scriban templates

Use Scriban when a managed file or lifecycle script argument must include a
value chosen by the pack consumer. Keep static content as a normal managed file.

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
    values:
      - library
      - service
```

Consumers pass values with repeatable `--parameter` or `-p` options:

```powershell
luna install company-foundation \
  -p companyName="Lunaris Digital Solutions" \
  -p projectType=service
```

Matching values in the project-level `variables` mapping are used when no
explicit value is supplied. Explicit command arguments take precedence.

## Render managed files

Set `template: true` only for selectors whose UTF-8 content uses Scriban.

```yml
managedFiles:
  - source: templates/NOTICE.md
    target: NOTICE.md
    template: true
  - source: templates/workflow.yml
    target: .github/workflows/build.yml
    template: true
    condition: includeWorkflow
```

A template can use resolved parameter names and Scriban date functions:

```scriban
Copyright {{ date.now.year }} {{ companyName }}
Project type: {{ projectType }}
```

Template parsing is strict. An unknown variable, invalid expression, or invalid
UTF-8 input fails planning before project files or state change. Templates do
not receive filesystem, host-service, include, or custom-function access.

## Render script arguments

Lifecycle script `arguments` are Scriban templates. Each rendered list item is
passed as one literal process argument; LunaPack does not concatenate arguments
into a shell command.

```yml
scripts:
  postInstall:
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

## Escape literal delimiters

For a template-enabled file that must emit literal `{{` or `}}`, use a Scriban
string expression:

```scriban
{{ "{{ literal }}" }}
```

Prefer leaving `template` unset when the complete file is literal. This also
allows binary content to copy without UTF-8 decoding.
