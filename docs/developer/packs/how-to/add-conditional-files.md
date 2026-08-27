# Add conditional files

Select pack files from values supplied by the consumer. Conditions control
whether a managed selector participates in install and update planning.

## Declare parameters

Create Boolean and enum parameters with authoring commands:

```powershell
luna pack set parameter includeCi bool `
  --display-name "Include CI workflow" `
  --description "Adds the GitHub Actions build workflow."
luna pack set parameter projectType enum `
  --required `
  --value library `
  --value service
```

## Attach conditions

Add selectors with `--condition` or `-c`:

```powershell
luna pack add file templates/build.yml `
  --target .github/workflows/build.yml `
  --condition includeCi
luna pack add file templates/service.json `
  --target config/service.json `
  --condition 'includeCi && projectType == "service"'
luna pack add file templates/library.props `
  --target Directory.Build.props `
  --condition 'projectType == "library"'
```

Conditions support Boolean names, `!` negation, `==` and `!=` comparisons with
quoted string or enum values, `&&`, `||`, and parentheses. Boolean parameters
cannot be compared to strings. Every referenced parameter must be declared.

Operator precedence evaluates `&&` before `||`. Use parentheses when intent is
not immediate:

```yml
condition: includeCi && (projectType == "service" || projectType == "library")
```

## Validate both branches

Check syntax locally, then install into disposable projects with values that
include and exclude each selector:

```powershell
luna pack validate
luna install application-foundation `
  -p includeCi=true `
  -p projectType=service `
  --dry-run
luna install application-foundation `
  -p includeCi=false `
  -p projectType=library `
  --dry-run
```

Keep conditions on selectors, not inside static file content. Use
[Scriban templates](use-scriban-templates.md) when selected file content must
also vary.
