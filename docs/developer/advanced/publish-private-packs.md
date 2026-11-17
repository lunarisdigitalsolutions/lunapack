# Publish packs from GitHub Actions

LunaPack provides three installable GitHub Actions workflow packs. Add the
LunaPack catalog, then install the workflow matching your registry:

```powershell
luna sources add github lunapack lunarisdigitalsolutions/lunapack --ref main --path projects/packs
luna install github-oci-publish-workflow@1.0.0
```

Choose `github-acr-publish-workflow@1.0.0` for Azure Container Registry or
`github-ghcr-publish-workflow@1.0.0` for GitHub Container Registry. Each pack
composes `github-publish-changed-packs@1.0.0`, which installs the shared
PowerShell publisher under `.github/scripts/`. You do not need to install the
helper separately.

## Configure the publishing source

Configure an OCI source in `lunapack.yml` whose name matches the workflow's
target:

```powershell
luna sources add oci generic https://registry.example.com --path packs
```

Use source name `acr` with `https://<registry-name>.azurecr.io` for ACR, or
source name `ghcr` with `https://ghcr.io` for GHCR. Keep project configuration
free of passwords and tokens. Configure the registry to reject overwrites of
published version tags, protect `main`, and review pack changes before they
can use registry write permissions.

## Choose registry authentication

| Workflow pack | Authentication                                                                                              | Runner requirements                                                                                                             |
| ------------- | ----------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Generic OCI   | Source-bound native credential store                                                                        | Persistent Linux self-hosted runner with PowerShell 7 (`pwsh`); preconfigure its user's native credential store.                |
| GHCR          | Source-bound native credential store                                                                        | Persistent Linux self-hosted runner with PowerShell 7 (`pwsh`); preconfigure its user's native credential store.                |
| ACR           | GitHub OIDC through `azure/login`; grant the federated identity the narrowest role that permits publication | GitHub-hosted or self-hosted runner with PowerShell 7 (`pwsh`).                                                                 |

The GHCR workflow does not use `GITHUB_TOKEN`.

Seed generic OCI or GHCR credentials with an interactive Luna operation on the
runner before enabling the workflow. The credential is bound to the project,
source name, registry, and repository path. Luna has no registry login command
or Luna-specific credential environment variables.

For ACR, configure a federated Azure identity restricted to the repository's
`main` branch and set repository variables `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, and `LUNA_VERSION`. The workflow
uses the Azure CLI identity established by `azure/login` through
`DefaultAzureCredential`; no permanent client secret is needed.

## Publish changed pack directories

The workflows run on pushes to `main` that change `packs/**`. They check out
full Git history and use a SHA-pinned `tj-actions/changed-files` action to
produce JSON paths. A shared PowerShell publisher maps each changed path to its
direct-child directory under `packs/`, publishes each existing directory with
a `pack.yml` once, and skips deleted packs. JSON preserves names containing
spaces. On a first push, the publisher scans all existing direct-child pack
directories because no earlier commit exists. Change-detection or publishing
failures stop the workflow.

Each pack directory is published with the target configured by its workflow:

```bash
luna publish packs/backend --target generic
```

Luna reads the pack ID and version from `pack.yml`, rejects invalid content and
existing version tags, and reports the published reference and verified
manifest digest. Bump the pack version before publishing changed content.
Registry-side immutable tags are still required to prevent later movement or
races between independent publishers.

The workflows serialize runs per target. If a run fails, check whether the
registry already received the release before fixing the source or pack version
and rerunning. Luna validates the pack before publishing and does not execute
its lifecycle hooks; consumers still need to review and authorize pack scripts
before installing them.

For source configuration, registry discovery limits, and credential behavior,
see [Add a pack source](../sources.md#add-an-oci-registry-source) and the
[environment reference](../cli/environment.md#oci-registry-authentication).
See the [command reference](../cli/commands.md#pack-publishing) for the full
`luna publish` contract.
