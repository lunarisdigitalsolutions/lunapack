# Add conditional files

Select pack files from values supplied by the consumer. Conditions control
whether a managed selector participates in install and update planning.

Run these authoring commands against the synthetic
`example-documentation-standard` pack created by
[Create a first pack](../tutorials/first-pack.md).

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
luna pack set parameter features enum `
  --multiple `
  --value api `
  --value docker
```

## Attach conditions

Add selectors with `--condition` or `-c`:

```powershell
luna pack add file targets/build.yml `
  --target .github/workflows/build.yml `
  --condition includeCi
luna pack add file targets/service.json `
  --target config/service.json `
  --condition 'includeCi && projectType == "service"'
luna pack add file targets/library.props `
  --target Directory.Build.props `
  --condition 'projectType == "library"'
luna pack add file targets/docker.yml `
  --target compose.yml `
  --condition '"docker" in features'
```

Conditions support Boolean names, `!` negation, `==` and `!=` comparisons with
quoted scalar string or enum values, multi-select membership as `"literal" in
identifier`, `&&`, `||`, and parentheses. Membership requires a declared
multi-select enum on the right. Boolean parameters cannot be compared to
strings, and multi-select parameters cannot use scalar equality.

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
luna install example-documentation-standard@1.0.0 `
  -p includeCi=true `
  -p projectType=service `
  --dry-run
luna install example-documentation-standard@1.0.0 `
  -p includeCi=false `
  -p projectType=library `
  --dry-run
```

Keep conditions on selectors, not inside static file content. Use
[Scriban templates](use-scriban-templates.md) when selected file content must
also vary.

For an external selector, a false condition also suppresses source resolution,
validation, approval, workspace configuration, and lock evidence when no other
selected file requires that alias. Test both branches because later parameter
values can activate the complete external-source workflow. See
[Use external Git files in a pack](use-external-pack-sources.md).
