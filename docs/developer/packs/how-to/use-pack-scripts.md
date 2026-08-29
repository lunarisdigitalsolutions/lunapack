# Add lifecycle scripts to a pack

Add lifecycle hooks only when managed files cannot complete the operation.
Scripts run with the consumer's permissions and may have effects LunaPack cannot
roll back.

## Choose a hook

Supported hooks are `preInstall`, `postInstall`, `preUpdate`, `postUpdate`,
`preUninstall`, and `postUninstall`. Use a direct command when no script file is
needed:

```powershell
luna pack add hook script command postInstall dotnet tool restore `
  --description "Restore repository-local .NET tools."
```

Use a packed script with an explicit runner when logic belongs in the pack:

```powershell
luna pack add hook script file preUpdate scripts/check.ps1 pwsh `
  -NoProfile `
  --description "Check update prerequisites."
```

The script path is relative to `pack.yml`. Each trailing value becomes one
literal process argument. LunaPack does not invoke an implicit shell. Add
`--replace` when changing an existing hook.

## Pass parameter values

Script arguments are Scriban templates, but the command, runner, and file are
literal:

```yml
hooks:
  preInstall:
    - type: script
      file: scripts/setup.ps1
      runner: pwsh
      arguments:
        - -ProjectType
        - '{{ projectType }}'
      description: Configure project tooling.
```

Rendered arguments are shown before authorization and remain separate argv
values.

## Inspect and test hooks

```powershell
luna pack hooks
luna pack validate
luna install application-foundation --scripts skip --dry-run
luna install application-foundation --scripts prompt
```

Prompt mode asks consumers to approve untrusted hooks. `--scripts run` permits
all non-disabled hooks for that invocation; `--scripts skip` runs none. Test
approved hooks only in a disposable repository. Keep hooks deterministic,
non-interactive, cross-platform where practical, and explicit about network or
filesystem effects.

Composite packs can suppress dependency hooks with `--disable-hook`:

```powershell
luna pack add reference dotnet-csharpier-tool 1.0.0 `
  --disable-hook postInstall
```
